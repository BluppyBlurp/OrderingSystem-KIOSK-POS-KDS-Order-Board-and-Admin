import type { Media, MediaType } from "../api";

/** Mirrors the API's limits (Media:* settings); the API checks again after the upload. */
export const IMAGE_TYPES = ["image/jpeg", "image/png", "image/webp"];
export const VIDEO_TYPES = ["video/mp4"];
export const MAX_IMAGE_MB = 10;
export const MAX_VIDEO_MB = 50;

/** Which kind of media a file is, or why it can't be uploaded. */
export function classifyFile(file: { type: string; size: number }): { type: MediaType } | { error: string } {
  if (IMAGE_TYPES.includes(file.type)) {
    return file.size > MAX_IMAGE_MB * 1024 * 1024 ? { error: `Images can be at most ${MAX_IMAGE_MB} MB.` } : { type: "Image" };
  }
  if (VIDEO_TYPES.includes(file.type)) {
    return file.size > MAX_VIDEO_MB * 1024 * 1024 ? { error: `Videos can be at most ${MAX_VIDEO_MB} MB.` } : { type: "Video" };
  }
  return { error: "Use a JPEG, PNG or WebP image, or an MP4 video." };
}

export interface UploadSteps {
  presign: (body: { type: MediaType; contentType: string; sizeBytes: number }) => Promise<{ key: string; uploadUrl: string; contentType: string }>;
  put: (url: string, file: Blob, contentType: string) => Promise<Response>;
  complete: (body: { key: string; type: MediaType; sortOrder: number }) => Promise<Media>;
}

/**
 * The three-step upload (docs §3.2): ask the API for a presigned URL, PUT the file straight to R2, then tell the API
 * so it can check the file and make the WebP variants.
 */
export async function uploadMedia(file: File, sortOrder: number, steps: UploadSteps): Promise<Media> {
  const kind = classifyFile(file);
  if ("error" in kind) throw new Error(kind.error);

  const upload = await steps.presign({ type: kind.type, contentType: file.type, sizeBytes: file.size });
  const res = await steps.put(upload.uploadUrl, file, upload.contentType);
  if (!res.ok) throw new Error(`Upload to storage failed (${res.status}). Check the bucket's CORS rule allows PUT from this site.`);
  return steps.complete({ key: upload.key, type: kind.type, sortOrder });
}

export const putToStorage: UploadSteps["put"] = (url, file, contentType) =>
  fetch(url, { method: "PUT", body: file, headers: { "Content-Type": contentType } });
