import { useEffect, useState } from "react";
import type { Menu } from "../api";
import { ProductImage } from "../components/ProductImage";
import { Button, delta, money, Screen } from "../components/ui";
import { makeLine, MAX_QUANTITY } from "../lib/cart";
import { initialSelection, isGroupValid, pickHint, selectedModifiers, toggle, unitPrice, type Product } from "../lib/customize";
import { useKiosk } from "../store";

/**
 * One question per screen, in the order the manager set (e.g. drink → upsize drink → upsize fries →
 * fries flavor → add-ons), then a final quantity + summary step with "Add to tray".
 */
export function CustomizeScreen({ menu }: { menu: Menu }) {
  const { customizingProductId, addToCart, closeCustomize } = useKiosk();
  const category = menu.categories.find((c) => c.products.some((p) => p.id === customizingProductId));
  const product = category?.products.find((p) => p.id === customizingProductId);

  // The product vanished from the menu (e.g. removed by a manager mid-order): back to the menu.
  useEffect(() => {
    if (!product) closeCustomize();
  }, [product, closeCustomize]);

  if (!product || !category) return null;
  return <Wizard key={product.id} product={product} categoryName={category.name} onAdd={addToCart} onCancel={closeCustomize} />;
}

/** Progress through the questions, as a row of bars rather than a count. */
function Steps({ total, at }: { total: number; at: number }) {
  if (total === 0) return null;
  return (
    <div className="flex gap-1" aria-label={`Step ${at + 1} of ${total + 1}`}>
      {Array.from({ length: total + 1 }, (_, i) => (
        <span key={i} className={`h-2 w-6 sm:w-10 ${i <= at ? "bg-brand" : "bg-paper-deep"}`} />
      ))}
    </div>
  );
}

function Wizard({
  product,
  categoryName,
  onAdd,
  onCancel,
}: {
  product: Product;
  categoryName: string;
  onAdd: ReturnType<typeof useKiosk.getState>["addToCart"];
  onCancel: () => void;
}) {
  const groups = product.modifierGroups;
  const [step, setStep] = useState(0); // 0..groups.length-1 = questions; groups.length = summary
  const [selection, setSelection] = useState(() => initialSelection(product));
  const [quantity, setQuantity] = useState(1);

  const group = groups[step];
  const price = unitPrice(product, selection);
  const back = () => (step === 0 ? onCancel() : setStep(step - 1));

  const header = (
    <div className="flex items-center gap-4 border-b-3 border-line bg-card p-3 sm:p-5">
      <ProductImage name={product.name} category={categoryName} media={product.media} className="h-16 w-20 shrink-0 sm:h-20 sm:w-28" />
      <div className="min-w-0 flex-1">
        <p className="truncate display text-[length:var(--text-step-2)] leading-tight">
          {product.name}
        </p>
        <p className="text-[length:var(--text-step-0)] text-ink-soft">{money(price)} each</p>
      </div>
      <Steps total={groups.length} at={step} />
    </div>
  );

  if (group) {
    const picked = selection[group.id] ?? [];
    const valid = isGroupValid(group, selection);
    const optional = picked.length === 0 && group.minSelect === 0;
    return (
      <Screen
        onBack={back}
        footer={
          <div className="flex gap-3">
            <Button size="lg" onClick={onCancel}>
              Cancel
            </Button>
            <Button
              variant="primary"
              size="lg"
              className="notch-sm ml-auto flex-1 sm:flex-none sm:min-w-64"
              disabled={!valid}
              onClick={() => setStep(step + 1)}
            >
              {optional ? "No thanks" : "Next"}
            </Button>
          </div>
        }
      >
        {header}
        <div className="p-4 sm:p-6">
          <h2 className="display text-[length:var(--text-step-3)] leading-tight">{group.name}</h2>
          <p className="mb-5 text-[length:var(--text-step-0)] text-ink-soft">{pickHint(group)}</p>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 sm:gap-4 2xl:grid-cols-4">
            {group.modifiers.map((m) => {
              const on = picked.includes(m.id);
              return (
                <button
                  key={m.id}
                  type="button"
                  disabled={!m.isAvailable}
                  aria-pressed={on}
                  onClick={() => setSelection((s) => toggle(s, group, m.id))}
                  className={`press flex min-h-24 flex-col items-center justify-center gap-1 border-3 p-3 text-center text-[length:var(--text-step-1)] font-semibold disabled:opacity-30 sm:min-h-28 ${
                    on ? "border-brand bg-accent text-ink" : "border-line bg-card"
                  }`}
                >
                  {m.name}
                  <span className="text-[length:var(--text-step-0)] font-normal text-ink-soft">
                    {m.isAvailable ? delta(m.priceDelta) || "Included" : "Sold out"}
                  </span>
                </button>
              );
            })}
          </div>
        </div>
      </Screen>
    );
  }

  const chosen = selectedModifiers(product, selection);
  return (
    <Screen
      onBack={groups.length ? back : onCancel}
      footer={
        <div className="flex gap-3">
          <Button size="lg" onClick={onCancel}>
            Cancel
          </Button>
          <Button
            variant="primary"
            size="lg"
            className="notch-sm ml-auto flex-1 sm:flex-none sm:min-w-80"
            onClick={() => onAdd(makeLine(product, selection, quantity))}
          >
            Add to tray · {money(price * quantity)}
          </Button>
        </div>
      }
    >
      {header}
      <div className="mx-auto flex max-w-2xl flex-col items-center gap-7 p-5 sm:p-8">
        <div className="w-full">
          <h2 className="mb-3 display text-[length:var(--text-step-2)]">What you're getting</h2>
          {chosen.length === 0 ? (
            <p className="text-[length:var(--text-step-1)]">{product.name}</p>
          ) : (
            <ul className="border-3 border-line bg-card">
              {chosen.map((m) => (
                <li
                  key={m.id}
                  className="flex justify-between gap-4 border-b-2 border-paper-deep px-4 py-3 text-[length:var(--text-step-1)] last:border-b-0"
                >
                  <span>{m.name}</span>
                  <span className="text-ink-soft">{delta(m.priceDelta)}</span>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="flex items-center gap-5">
          <Button size="lg" className="w-16" disabled={quantity <= 1} onClick={() => setQuantity(quantity - 1)} aria-label="One less">
            −
          </Button>
          <span className="w-20 text-center display text-[length:var(--text-step-4)] tabular-nums">
            {quantity}
          </span>
          <Button
            size="lg"
            className="w-16"
            disabled={quantity >= MAX_QUANTITY}
            onClick={() => setQuantity(quantity + 1)}
            aria-label="One more"
          >
            +
          </Button>
        </div>
      </div>
    </Screen>
  );
}
