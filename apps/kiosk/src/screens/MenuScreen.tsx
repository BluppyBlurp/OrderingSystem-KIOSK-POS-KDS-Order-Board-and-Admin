import type { Menu } from "../api";
import { Button, money, Screen, Shape } from "../components/ui";
import { cartCount, cartTotal } from "../lib/cart";
import { useKiosk } from "../store";

export function OrderSummaryBadge() {
  const { diningOption, orderType, tableNumber } = useKiosk();
  const where = orderType === "ServeToTable" ? `Table ${tableNumber}` : "Pick up at counter";
  return (
    <span className="border-4 border-black px-3 py-1 text-lg font-bold uppercase">
      {diningOption === "TakeOut" ? "Take out" : "Dine in"} · {where}
    </span>
  );
}

export function MenuScreen({ menu }: { menu: Menu }) {
  const { activeCategoryId, setCategory, customize, cart, go, reset } = useKiosk();
  const active = menu.categories.find((c) => c.id === activeCategoryId) ?? menu.categories[0];

  return (
    <Screen
      footer={
        <div className="flex items-center justify-between gap-4">
          <Button variant="ghost" onClick={reset}>
            Start over
          </Button>
          <OrderSummaryBadge />
          <Button variant="solid" size="lg" disabled={cart.length === 0} onClick={() => go("cart")}>
            Cart ({cartCount(cart)}) · {money(cartTotal(cart))}
          </Button>
        </div>
      }
    >
      <div className="flex h-full">
        <nav className="flex w-64 shrink-0 flex-col gap-3 overflow-y-auto border-r-4 border-black p-4" aria-label="Categories">
          {menu.categories.map((c) => (
            <Button key={c.id} variant={c.id === active?.id ? "solid" : "outline"} className="text-left" onClick={() => setCategory(c.id)}>
              {c.name}
            </Button>
          ))}
        </nav>

        <section className="grid flex-1 auto-rows-min grid-cols-3 gap-4 overflow-y-auto p-4">
          {active?.products.map((p) => (
            <button
              key={p.id}
              type="button"
              disabled={p.isSoldOut}
              onClick={() => customize(p.id)}
              className="relative flex min-h-64 flex-col items-center gap-3 border-4 border-black p-4 text-center active:translate-y-0.5 disabled:opacity-40"
            >
              <div className="flex h-28 w-full items-center justify-center">
                {p.media[0]?.type === "Image" ? (
                  <img src={p.media[0].thumbnailUrl ?? p.media[0].url} alt="" className="h-full object-contain grayscale" />
                ) : (
                  <Shape kind={active.name} />
                )}
              </div>
              <span className="text-xl font-bold leading-tight">{p.name}</span>
              {p.description && <span className="text-sm">{p.description}</span>}
              <span className="mt-auto text-2xl font-black">{money(p.price)}</span>
              {/* Sold-out items stay in place, greyed, so nothing jumps around mid-order (docs §12). */}
              {p.isSoldOut && (
                <span className="absolute inset-x-0 top-1/3 -rotate-12 border-y-4 border-black bg-white py-2 text-2xl font-black">
                  SOLD OUT
                </span>
              )}
            </button>
          ))}
        </section>
      </div>
    </Screen>
  );
}
