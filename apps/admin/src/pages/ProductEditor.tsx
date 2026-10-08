import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import {
  addMediaByUrl,
  completeMediaUpload,
  createProduct,
  deleteMedia,
  errorText,
  keys,
  presignMedia,
  setStock,
  updateProduct,
  useModifierGroups,
  type Category,
  type MediaType,
  type Product,
} from "../api";
import { Check, ErrorBox, Field, inputClass, parseNumber, TextInput } from "../components/form";
import { Button } from "../components/ui";
import { move } from "../lib/reorder";
import { putToStorage, uploadMedia } from "../lib/upload";

/**
 * Create or edit one product. Stock is set on create, then only through "Set stock" (the API never lets a product
 * edit overwrite stock that orders are reserving). Media can be added once the product exists.
 */
export function ProductEditor({
  product,
  categories,
  defaultCategoryId,
  nextSortOrder,
  onClose,
  onSaved,
}: {
  product: Product | null;
  categories: Category[];
  defaultCategoryId: string;
  nextSortOrder: number;
  onClose: () => void;
  onSaved: (product: Product) => void;
}) {
  const queryClient = useQueryClient();
  const groups = useModifierGroups();
  const [categoryId, setCategoryId] = useState(product?.categoryId ?? defaultCategoryId);
  const [name, setName] = useState(product?.name ?? "");
  const [description, setDescription] = useState(product?.description ?? "");
  const [price, setPrice] = useState(product ? String(product.basePrice) : "");
  const [isAvailable, setIsAvailable] = useState(product?.isAvailable ?? true);
  const [groupIds, setGroupIds] = useState<string[]>(product?.modifierGroupIds ?? []);
  const [trackStock, setTrackStock] = useState(product ? product.stock !== null : false);
  const [stock, setStockText] = useState(product?.stock != null ? String(product.stock) : "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: keys.products });

  const run = async (action: () => Promise<void>, success?: string) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
      if (success) setNotice(success);
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
      await refresh();
    }
  };

  const save = () =>
    run(async () => {
      const basePrice = parseNumber(price);
      if (basePrice === null) throw new Error("Enter a price.");
      const body = {
        categoryId,
        name: name.trim(),
        description: description.trim() || null,
        basePrice,
        isAvailable,
        sortOrder: product?.sortOrder ?? nextSortOrder,
        modifierGroupIds: groupIds,
        stock: product ? product.stock : trackStock ? (parseNumber(stock) ?? 0) : null,
      };
      const saved = product ? await updateProduct(product.id, body) : await createProduct(body);
      await queryClient.invalidateQueries({ queryKey: keys.categories });
      onSaved(saved);
    }, "Saved.");

  const saveStock = () =>
    run(async () => {
      if (!product) return;
      await setStock(product.id, trackStock ? (parseNumber(stock) ?? 0) : null);
    }, "Stock updated.");

  const allGroups = groups.data ?? [];
  const toggleGroup = (id: string, on: boolean) => setGroupIds((ids) => (on ? [...ids, id] : ids.filter((x) => x !== id)));

  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-white/70" role="dialog" aria-label={product ? `Edit ${product.name}` : "New product"}>
      <div className="flex h-full w-full max-w-3xl flex-col gap-4 overflow-y-auto border-l-8 border-black bg-white p-6">
        <div className="flex items-center justify-between">
          <h2 className="text-3xl font-black uppercase">{product ? `Edit ${product.name}` : "New product"}</h2>
          <Button onClick={onClose}>Close</Button>
        </div>
        <ErrorBox message={error} onDismiss={() => setError(null)} />
        {notice && <p className="border-4 border-black p-2 font-bold">{notice}</p>}

        <Field label="Category">
          <select className={inputClass} value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Name">
          <TextInput value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="Description">
          <TextInput value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <Field label="Price (₱, VAT included)">
          <TextInput inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} />
        </Field>
        <Check label="Available on the kiosk" checked={isAvailable} onChange={setIsAvailable} />

        <fieldset className="border-4 border-black p-3">
          <legend className="px-2 font-bold uppercase">Stock</legend>
          <Check label="Track stock (sells out at 0)" checked={trackStock} onChange={setTrackStock} />
          {trackStock && (
            <Field label="Units in stock">
              <TextInput inputMode="numeric" value={stock} onChange={(e) => setStockText(e.target.value)} />
            </Field>
          )}
          {product && (
            <Button className="mt-2" disabled={busy} onClick={saveStock}>
              Set stock
            </Button>
          )}
          {product && <p className="mt-1 text-sm">Stock changes save on their own, with "Set stock".</p>}
        </fieldset>

        <fieldset className="border-4 border-black p-3">
          <legend className="px-2 font-bold uppercase">Questions (option groups), in the order the kiosk asks them</legend>
          {groupIds.map((id, i) => {
            const group = allGroups.find((g) => g.id === id);
            return (
              <div key={id} className="flex items-center gap-2 py-1">
                <Button disabled={i === 0} onClick={() => setGroupIds(move(groupIds, i, -1))} aria-label="Ask earlier">
                  ↑
                </Button>
                <Button disabled={i === groupIds.length - 1} onClick={() => setGroupIds(move(groupIds, i, 1))} aria-label="Ask later">
                  ↓
                </Button>
                <span className="flex-1 text-lg font-bold">
                  {i + 1}. {group?.name ?? "…"}
                </span>
                <Button onClick={() => toggleGroup(id, false)}>Remove</Button>
              </div>
            );
          })}
          <select
            className={`${inputClass} mt-2`}
            value=""
            onChange={(e) => e.target.value && toggleGroup(e.target.value, true)}
            aria-label="Add a question"
          >
            <option value="">+ Add a question…</option>
            {allGroups
              .filter((g) => !groupIds.includes(g.id))
              .map((g) => (
                <option key={g.id} value={g.id}>
                  {g.name} ({g.modifiers.length} options)
                </option>
              ))}
          </select>
        </fieldset>

        <Button variant="solid" size="lg" disabled={busy || !name.trim()} onClick={save}>
          {busy ? "Saving…" : product ? "Save changes" : "Create product"}
        </Button>

        {product && <MediaSection product={product} busy={busy} run={run} />}
      </div>
    </div>
  );
}

function MediaSection({
  product,
  busy,
  run,
}: {
  product: Product;
  busy: boolean;
  run: (action: () => Promise<void>, success?: string) => Promise<void>;
}) {
  const [url, setUrl] = useState("");
  const [urlType, setUrlType] = useState<MediaType>("Image");

  const upload = (file: File) =>
    run(async () => {
      await uploadMedia(file, product.media.length, {
        presign: presignMedia,
        put: putToStorage,
        complete: (body) => completeMediaUpload(product.id, body),
      });
    }, "Uploaded.");

  return (
    <fieldset className="border-4 border-black p-3">
      <legend className="px-2 font-bold uppercase">Photos and videos</legend>
      <div className="flex flex-wrap gap-3">
        {product.media.map((m) => (
          <div key={m.id} className="flex w-44 flex-col gap-2 border-4 border-black p-2">
            {m.type === "Image" ? (
              <img src={m.thumbnailUrl ?? m.url} alt="" className="h-28 w-full object-contain" />
            ) : (
              <video src={m.url} muted className="h-28 w-full object-contain" />
            )}
            <a href={m.url} target="_blank" rel="noreferrer" className="truncate text-sm underline">
              {m.type}
            </a>
            <Button disabled={busy} onClick={() => run(() => deleteMedia(product.id, m.id), "Removed.")}>
              Remove
            </Button>
          </div>
        ))}
        {product.media.length === 0 && <p>No media yet. The kiosk shows a placeholder shape.</p>}
      </div>

      <Field label="Upload (JPEG, PNG or WebP up to 10 MB; MP4 up to 50 MB and 30 s)">
        <input
          type="file"
          accept="image/jpeg,image/png,image/webp,video/mp4"
          disabled={busy}
          className={inputClass}
          onChange={(e) => {
            const file = e.target.files?.[0];
            e.target.value = "";
            if (file) void upload(file);
          }}
        />
      </Field>

      <form
        className="mt-3 flex items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          void run(async () => {
            await addMediaByUrl(product.id, { type: urlType, url: url.trim(), thumbnailUrl: null, sortOrder: product.media.length });
            setUrl("");
          }, "Added.");
        }}
      >
        <Field label="Or add by https URL">
          <TextInput value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://…" />
        </Field>
        <select className={`${inputClass} w-36`} value={urlType} onChange={(e) => setUrlType(e.target.value as MediaType)} aria-label="Media type">
          <option value="Image">Image</option>
          <option value="Video">Video</option>
        </select>
        <Button type="submit" disabled={busy || !url.trim()}>
          Add
        </Button>
      </form>
    </fieldset>
  );
}
