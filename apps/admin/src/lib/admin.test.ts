import { describe, expect, it, vi } from "vitest";
import { move } from "./reorder";
import { classifyFile, uploadMedia } from "./upload";

describe("move", () => {
  it("swaps with the neighbour", () => {
    expect(move(["a", "b", "c"], 1, -1)).toEqual(["b", "a", "c"]);
    expect(move(["a", "b", "c"], 1, 1)).toEqual(["a", "c", "b"]);
  });

  it("does nothing at the edges", () => {
    const items = ["a", "b"];
    expect(move(items, 0, -1)).toBe(items);
    expect(move(items, 1, 1)).toBe(items);
  });
});

describe("classifyFile", () => {
  const MB = 1024 * 1024;
  it("accepts the API's image and video types within their limits", () => {
    expect(classifyFile({ type: "image/png", size: 2 * MB })).toEqual({ type: "Image" });
    expect(classifyFile({ type: "video/mp4", size: 40 * MB })).toEqual({ type: "Video" });
  });

  it("rejects other types and oversized files before uploading", () => {
    expect(classifyFile({ type: "image/gif", size: 1 })).toHaveProperty("error");
    expect(classifyFile({ type: "image/jpeg", size: 11 * MB })).toHaveProperty("error");
    expect(classifyFile({ type: "video/mp4", size: 51 * MB })).toHaveProperty("error");
  });
});

describe("uploadMedia", () => {
  const file = new File([new Uint8Array(10)], "burger.png", { type: "image/png" });
  const media = { id: "m1", type: "Image" as const, url: "https://media.test/x.webp", thumbnailUrl: null };

  it("presigns, PUTs with the same content type, then completes with the key", async () => {
    const steps = {
      presign: vi.fn().mockResolvedValue({ key: "uploads/abc", uploadUrl: "https://r2.test/put", contentType: "image/png" }),
      put: vi.fn().mockResolvedValue(new Response(null, { status: 200 })),
      complete: vi.fn().mockResolvedValue(media),
    };
    await expect(uploadMedia(file, 2, steps)).resolves.toBe(media);
    expect(steps.presign).toHaveBeenCalledWith({ type: "Image", contentType: "image/png", sizeBytes: 10 });
    expect(steps.put).toHaveBeenCalledWith("https://r2.test/put", file, "image/png");
    expect(steps.complete).toHaveBeenCalledWith({ key: "uploads/abc", type: "Image", sortOrder: 2 });
  });

  it("stops when the storage PUT fails", async () => {
    const steps = {
      presign: vi.fn().mockResolvedValue({ key: "uploads/abc", uploadUrl: "https://r2.test/put", contentType: "image/png" }),
      put: vi.fn().mockResolvedValue(new Response(null, { status: 403 })),
      complete: vi.fn(),
    };
    await expect(uploadMedia(file, 0, steps)).rejects.toThrow(/403/);
    expect(steps.complete).not.toHaveBeenCalled();
  });
});
