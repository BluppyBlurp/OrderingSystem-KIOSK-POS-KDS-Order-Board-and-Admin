import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import {
  createCategory,
  deleteCategory,
  deleteProduct,
  errorText,
  keys,
  reorderCategories,
  reorderProducts,
  setAvailability,
  setStock,
  updateCategory,
  useCategories,
  useProducts,
  type Category,
  type Product,
} from "../api";
import { Check, ErrorBox, TextInput } from "../components/form";
import { Button, money } from "../components/ui";
import { move } from "../lib/reorder";
import { ProductEditor } from "./ProductEditor";

/**
 * Categories on the left, the selected category's products on the right. Every save pushes MenuChanged to kiosks.
 * Without `canEdit` (assistant managers) the page is read-only apart from availability and stock.
 */
export function MenuPage({ canEdit }: { canEdit: boolean }) {
  const queryClient = useQueryClient();
  const categories = useCategories();
  const products = useProducts();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editing, setEditing] = useState<Product | "new" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [newCategory, setNewCategory] = useState("");

  const list = categories.data ?? [];
  const selected = list.find((c) => c.id === selectedId) ?? list[0] ?? null;
  const inCategory = (products.data ?? []).filter((p) => p.categoryId === selected?.id);

  const run = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      await action();
    } catch (e) {
      setError(errorText(e));
    } finally {
      await queryClient.invalidateQueries({ queryKey: keys.categories });
      await queryClient.invalidateQueries({ queryKey: keys.products });
    }
  };

  return (
    <div className="flex h-full flex-col">
      <div className="p-4 pb-0">
        <ErrorBox message={error} onDismiss={() => setError(null)} />
      </div>
      <div className="flex min-h-0 flex-1">
        <aside className="flex w-96 shrink-0 flex-col gap-3 overflow-y-auto border-r-4 border-black p-4">
          <h2 className="text-xl font-black uppercase">Categories</h2>
          {categories.isLoading && <p>Loading…</p>}
          {list.map((c, i) => (
            <CategoryRow
              key={c.id}
              category={c}
              selected={c.id === selected?.id}
              onSelect={() => setSelectedId(c.id)}
              onMove={(dir) => run(() => reorderCategories(move(list, i, dir).map((x) => x.id)))}
              onSave={(name, isActive) => run(() => updateCategory(c.id, { name, isActive, sortOrder: c.sortOrder }))}
              onDelete={() => confirm(`Delete category "${c.name}"?`) && run(() => deleteCategory(c.id))}
              first={i === 0}
              last={i === list.length - 1}
              canEdit={canEdit}
            />
          ))}
          {canEdit && (
          <form
            className="flex gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              if (!newCategory.trim()) return;
              void run(async () => {
                const created = await createCategory({ name: newCategory.trim(), sortOrder: list.length, isActive: true });
                setNewCategory("");
                setSelectedId(created.id);
              });
            }}
          >
            <TextInput placeholder="New category" value={newCategory} onChange={(e) => setNewCategory(e.target.value)} />
            <Button type="submit" variant="solid">
              Add
            </Button>
          </form>
          )}
        </aside>

        <section className="min-w-0 flex-1 overflow-y-auto p-4">
          {selected ? (
            <>
              <div className="mb-4 flex items-center justify-between">
                <h2 className="text-2xl font-black uppercase">{selected.name}</h2>
                {canEdit && (
                  <Button variant="solid" onClick={() => setEditing("new")}>
                    New product
                  </Button>
                )}
              </div>
              <table className="w-full border-collapse text-left text-lg">
                <thead>
                  <tr className="border-b-4 border-black">
                    {canEdit && <th className="p-2">Order</th>}
                    <th className="p-2">Name</th>
                    <th className="p-2">Price</th>
                    <th className="p-2">Stock</th>
                    <th className="p-2">Available</th>
                    <th className="p-2" />
                  </tr>
                </thead>
                <tbody>
                  {inCategory.map((p, i) => (
                    <tr key={p.id} className="border-b-2 border-black">
                      {canEdit && (
                      <td className="p-2 whitespace-nowrap">
                        <Button disabled={i === 0} aria-label="Move up" onClick={() => run(() => reorderProducts(move(inCategory, i, -1).map((x) => x.id)))}>
                          ↑
                        </Button>{" "}
                        <Button
                          disabled={i === inCategory.length - 1}
                          aria-label="Move down"
                          onClick={() => run(() => reorderProducts(move(inCategory, i, 1).map((x) => x.id)))}
                        >
                          ↓
                        </Button>
                      </td>
                      )}
                      <td className="p-2 font-bold">
                        {p.name}
                        {p.media.length > 0 && <span className="ml-2 text-sm font-normal">({p.media.length} media)</span>}
                      </td>
                      <td className="p-2">{money(p.basePrice)}</td>
                      <td className="p-2">
                        <StockCell stock={p.stock ?? null} onSave={(value) => run(() => setStock(p.id, value))} />
                      </td>
                      <td className="p-2">
                        <Check label={p.isAvailable ? "Yes" : "No"} checked={p.isAvailable} onChange={(v) => run(() => setAvailability(p.id, v))} />
                      </td>
                      <td className="p-2 text-right whitespace-nowrap">
                        {canEdit && (
                          <>
                            <Button onClick={() => setEditing(p)}>Edit</Button>{" "}
                            <Button onClick={() => confirm(`Delete "${p.name}"?`) && run(() => deleteProduct(p.id))}>Delete</Button>
                          </>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {inCategory.length === 0 && !products.isLoading && <p className="p-4 text-lg">No products in this category yet.</p>}
            </>
          ) : (
            !categories.isLoading && <p className="text-xl">{canEdit ? "Add a category to start building the menu." : "The menu is empty."}</p>
          )}
        </section>
      </div>

      {canEdit && editing && selected && (
        <ProductEditor
          // re-read the product after media changes so the editor shows the latest list
          product={editing === "new" ? null : ((products.data ?? []).find((p) => p.id === editing.id) ?? editing)}
          categories={list}
          defaultCategoryId={selected.id}
          nextSortOrder={inCategory.length}
          onClose={() => setEditing(null)}
          onSaved={(p) => {
            setEditing(p);
            setSelectedId(p.categoryId);
          }}
        />
      )}
    </div>
  );
}

function CategoryRow({
  category,
  selected,
  onSelect,
  onMove,
  onSave,
  onDelete,
  first,
  last,
  canEdit,
}: {
  category: Category;
  selected: boolean;
  onSelect: () => void;
  onMove: (dir: -1 | 1) => void;
  onSave: (name: string, isActive: boolean) => void;
  onDelete: () => void;
  first: boolean;
  last: boolean;
  canEdit: boolean;
}) {
  const [renaming, setRenaming] = useState(false);
  const [name, setName] = useState(category.name);

  return (
    <div className={`flex flex-col gap-2 border-4 border-black p-2 ${selected ? "bg-black text-white" : ""}`}>
      {renaming ? (
        <form
          className="flex gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            onSave(name.trim(), category.isActive);
            setRenaming(false);
          }}
        >
          <input className="w-full border-4 border-black px-2 text-lg text-black" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          <Button type="submit">Save</Button>
        </form>
      ) : (
        <button type="button" className="min-h-12 text-left text-xl font-bold" onClick={onSelect}>
          {category.name} <span className="text-sm font-normal">({category.productCount}{category.isActive ? "" : ", hidden"})</span>
        </button>
      )}
      {selected && canEdit && (
        <div className="flex flex-wrap gap-2 text-black">
          <Button disabled={first} onClick={() => onMove(-1)} aria-label="Move up">
            ↑
          </Button>
          <Button disabled={last} onClick={() => onMove(1)} aria-label="Move down">
            ↓
          </Button>
          <Button onClick={() => setRenaming(!renaming)}>Rename</Button>
          <Button onClick={() => onSave(category.name, !category.isActive)}>{category.isActive ? "Hide" : "Show"}</Button>
          <Button onClick={onDelete}>Delete</Button>
        </div>
      )}
    </div>
  );
}

/** Quick stock change from the list (the main job for assistant managers). Blank = stop tracking stock. */
function StockCell({ stock, onSave }: { stock: number | null; onSave: (value: number | null) => void }) {
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState("");

  if (!editing) {
    return (
      <span className="flex items-center gap-2 whitespace-nowrap">
        {stock ?? "not tracked"}
        <Button
          onClick={() => {
            setText(stock === null ? "" : String(stock));
            setEditing(true);
          }}
        >
          Change
        </Button>
      </span>
    );
  }
  return (
    <form
      className="flex items-center gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        const value = text.trim() === "" ? null : Math.max(0, Math.floor(Number(text)));
        if (value !== null && Number.isNaN(value)) return;
        onSave(value);
        setEditing(false);
      }}
    >
      <input className="w-24 border-4 border-black px-2 py-1 text-lg" inputMode="numeric" value={text} onChange={(e) => setText(e.target.value)} autoFocus aria-label="Stock" placeholder="none" />
      <Button type="submit" variant="solid">
        Save
      </Button>
      <Button onClick={() => setEditing(false)}>Cancel</Button>
    </form>
  );
}
