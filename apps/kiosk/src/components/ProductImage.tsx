import { useState } from "react";
import { FoodArt } from "./FoodArt";

type Media = { type?: string; url?: string | null; thumbnailUrl?: string | null };

/**
 * A product's picture: whatever a manager uploaded in Admin, or the built-in drawing when there is
 * none. The drawing also covers an upload that fails to load, so a broken URL never leaves a hole in
 * the menu mid-order.
 */
export function ProductImage({
  name,
  category,
  media,
  className = "",
}: {
  name: string;
  category?: string;
  media?: Media[] | null;
  className?: string;
}) {
  const [failed, setFailed] = useState(false);
  const image = media?.find((m) => m.type === "Image" && (m.thumbnailUrl ?? m.url));
  const src = image?.thumbnailUrl ?? image?.url;

  if (src && !failed) {
    return <img src={src} alt="" className={`${className} object-contain`} onError={() => setFailed(true)} />;
  }
  return <FoodArt name={name} category={category} className={className} />;
}
