import { useEffect, useState } from "react";
import type { Menu } from "../api";
import { Button, delta, money, Screen, Shape } from "../components/ui";
import { makeLine, MAX_QUANTITY } from "../lib/cart";
import { initialSelection, isGroupValid, pickHint, selectedModifiers, toggle, unitPrice, type Product } from "../lib/customize";
import { useKiosk } from "../store";

/**
 * One question per screen, in the order the manager set (e.g. drink → upsize drink → upsize fries →
 * fries flavor → add-ons), then a final quantity + summary step with "Add to cart".
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
    <div className="flex items-center gap-6 border-b-4 border-black p-6">
      <div className="flex h-24 w-32 items-center justify-center">
        <Shape kind={categoryName} />
      </div>
      <div className="flex-1">
        <p className="text-3xl font-black">{product.name}</p>
        <p className="text-xl">{money(price)} each</p>
      </div>
      {groups.length > 0 && (
        <p className="text-lg font-bold">
          {Math.min(step + 1, groups.length + 1)} / {groups.length + 1}
        </p>
      )}
    </div>
  );

  if (group) {
    const picked = selection[group.id] ?? [];
    const valid = isGroupValid(group, selection);
    return (
      <Screen
        onBack={back}
        footer={
          <div className="flex justify-between gap-4">
            <Button size="lg" onClick={onCancel}>
              Cancel
            </Button>
            <Button variant="solid" size="lg" className="min-w-64" disabled={!valid} onClick={() => setStep(step + 1)}>
              {picked.length === 0 && group.minSelect === 0 ? "No, thanks" : "Next"}
            </Button>
          </div>
        }
      >
        {header}
        <div className="p-6">
          <h2 className="text-4xl font-black">{group.name}</h2>
          <p className="mb-6 text-xl">{pickHint(group)}</p>
          <div className="grid grid-cols-3 gap-4">
            {group.modifiers.map((m) => {
              const on = picked.includes(m.id);
              return (
                <button
                  key={m.id}
                  type="button"
                  disabled={!m.isAvailable}
                  aria-pressed={on}
                  onClick={() => setSelection((s) => toggle(s, group, m.id))}
                  className={`flex min-h-28 flex-col items-center justify-center gap-1 border-4 border-black p-4 text-2xl font-bold active:translate-y-0.5 disabled:opacity-30 ${
                    on ? "bg-black text-white" : "bg-white"
                  }`}
                >
                  {m.name}
                  <span className="text-lg font-normal">{m.isAvailable ? delta(m.priceDelta) : "Sold out"}</span>
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
        <div className="flex justify-between gap-4">
          <Button size="lg" onClick={onCancel}>
            Cancel
          </Button>
          <Button variant="solid" size="lg" className="min-w-80" onClick={() => onAdd(makeLine(product, selection, quantity))}>
            Add to cart · {money(price * quantity)}
          </Button>
        </div>
      }
    >
      {header}
      <div className="flex flex-col items-center gap-8 p-8">
        <div className="w-full max-w-2xl">
          <h2 className="mb-2 text-3xl font-black">Your order</h2>
          {chosen.length === 0 ? (
            <p className="text-xl">{product.name}</p>
          ) : (
            <ul className="text-xl">
              {chosen.map((m) => (
                <li key={m.id} className="flex justify-between border-b-2 border-black py-2">
                  <span>{m.name}</span>
                  <span>{delta(m.priceDelta)}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
        <div className="flex items-center gap-6">
          <Button size="lg" className="w-20" disabled={quantity <= 1} onClick={() => setQuantity(quantity - 1)} aria-label="Less">
            −
          </Button>
          <span className="w-24 text-center text-6xl font-black tabular-nums">{quantity}</span>
          <Button size="lg" className="w-20" disabled={quantity >= MAX_QUANTITY} onClick={() => setQuantity(quantity + 1)} aria-label="More">
            +
          </Button>
        </div>
      </div>
    </Screen>
  );
}
