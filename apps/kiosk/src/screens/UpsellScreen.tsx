import { useEffect } from "react";
import type { Menu } from "../api";
import { Button, money, Screen, Shape } from "../components/ui";
import { cartTotal } from "../lib/cart";
import { suggestUpsells } from "../lib/upsell";
import { useKiosk } from "../store";

/** "Anything else?" — asked once per order, only for what the cart doesn't already include. */
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
      footer={
        <div className="flex items-center justify-between gap-4">
          <span className="text-2xl font-black">Total {money(cartTotal(cart))}</span>
          <Button variant="solid" size="lg" className="min-w-80" onClick={() => go("checkout")}>
            No thanks, pay now
          </Button>
        </div>
      }
    >
      <div className="flex flex-col gap-8 p-6">
        {groups.map((group) => (
          <section key={group.kind}>
            <h2 className="mb-3 text-3xl font-black uppercase">{group.prompt}</h2>
            <div className="grid grid-cols-3 gap-4">
              {group.products.map((p) => (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => customize(p.id, "upsell")}
                  className="flex min-h-48 flex-col items-center gap-3 border-4 border-black p-4 text-center active:translate-y-0.5"
                >
                  <div className="flex h-20 w-full items-center justify-center">
                    {p.media[0]?.type === "Image" ? (
                      <img src={p.media[0].thumbnailUrl ?? p.media[0].url} alt="" className="h-full object-contain grayscale" />
                    ) : (
                      <Shape kind={group.categoryName} />
                    )}
                  </div>
                  <span className="text-xl font-bold leading-tight">{p.name}</span>
                  <span className="mt-auto text-2xl font-black">+ {money(p.price)}</span>
                </button>
              ))}
            </div>
          </section>
        ))}
      </div>
    </Screen>
  );
}
