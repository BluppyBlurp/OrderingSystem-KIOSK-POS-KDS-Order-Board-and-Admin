import { useEffect } from "react";
import type { Menu } from "../api";
import { ProductImage } from "../components/ProductImage";
import { Button, money, Screen } from "../components/ui";
import { cartTotal } from "../lib/cart";
import { suggestUpsells } from "../lib/upsell";
import { useKiosk } from "../store";
import { OrderSummaryBadge } from "./MenuScreen";

/** "Anything else?" — asked once per order, only for what the tray doesn't already include. */
export function UpsellScreen({ menu }: { menu: Menu }) {
  const { cart, customize, go } = useKiosk();
  const groups = suggestUpsells(menu, cart);

  // Everything suggested has been added (or sold out meanwhile): nothing left to ask.
  useEffect(() => {
    if (groups.length === 0) go("checkout");
  }, [groups.length, go]);

  return (
    <Screen
      title="Anything else?"
      onBack={() => go("cart")}
      badge={<OrderSummaryBadge />}
      footer={
        <div className="flex items-center gap-3">
          <span className="font-[family-name:var(--font-display)] text-[length:var(--text-step-1)]">
            {money(cartTotal(cart))}
          </span>
          <Button variant="primary" size="lg" className="notch-sm ml-auto flex-1 sm:flex-none sm:min-w-72" onClick={() => go("checkout")}>
            No thanks, pay now
          </Button>
        </div>
      }
    >
      <div className="flex flex-col gap-7 p-4 sm:p-6">
        {groups.map((group) => (
          <section key={group.kind}>
            <h2 className="mb-3 font-[family-name:var(--font-display)] text-[length:var(--text-step-2)]">{group.prompt}</h2>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 sm:gap-4 2xl:grid-cols-4">
              {group.products.map((p) => (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => customize(p.id, "upsell")}
                  className="press notch flex flex-col items-center gap-2 border-3 border-line bg-card p-3 text-center sm:p-4"
                >
                  <ProductImage name={p.name} category={group.categoryName} media={p.media} className="h-20 w-full sm:h-24" />
                  <span className="font-[family-name:var(--font-display)] text-[length:var(--text-step-1)] leading-tight">
                    {p.name}
                  </span>
                  <span className="mt-auto text-[length:var(--text-step-0)] text-ink-soft">add {money(p.price)}</span>
                </button>
              ))}
            </div>
          </section>
        ))}
      </div>
    </Screen>
  );
}
