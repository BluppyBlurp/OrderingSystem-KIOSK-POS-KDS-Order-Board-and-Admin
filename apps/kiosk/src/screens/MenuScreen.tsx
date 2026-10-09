import type { Menu } from "../api";
import { ProductImage } from "../components/ProductImage";
import { Button, money, Price, Screen } from "../components/ui";
import { cartCount, cartTotal } from "../lib/cart";
import { useKiosk } from "../store";

/** Where this order is going — carried in the brand strip so it stays visible the whole way through. */
export function OrderSummaryBadge() {
  const { diningOption, orderType, tableNumber } = useKiosk();
  if (!diningOption) return null;
  const where = orderType === "ServeToTable" ? `Table ${tableNumber}` : "At the counter";
  return (
    <span className="text-[length:var(--text-step-0)] text-paper/85">
      {diningOption === "TakeOut" ? "Take out" : "Eat here"} · {where}
    </span>
  );
}

export function MenuScreen({ menu }: { menu: Menu }) {
  const { activeCategoryId, setCategory, customize, cart, go, reset } = useKiosk();
  const active = menu.categories.find((c) => c.id === activeCategoryId) ?? menu.categories[0];
  const count = cartCount(cart);

  return (
    <Screen
      badge={<OrderSummaryBadge />}
      footer={
        <div className="flex items-center gap-3">
          <Button variant="quiet" onClick={reset}>
            Start over
          </Button>
          <Button
            variant="primary"
            size="lg"
            className="notch-sm ml-auto flex-1 sm:flex-none sm:min-w-80"
            disabled={count === 0}
            onClick={() => go("cart")}
          >
            {count === 0 ? "Your tray is empty" : `Review ${count} ${count === 1 ? "item" : "items"} · ${money(cartTotal(cart))}`}
          </Button>
        </div>
      }
    >
      <div className="flex h-full flex-col lg:flex-row">
        {/* Categories: a scrolling strip across the top on narrow screens, a rail once there is width. */}
        <nav
          aria-label="Menu categories"
          className="flex shrink-0 gap-2 overflow-x-auto border-b-3 border-line bg-card p-2 lg:w-60 lg:flex-col lg:gap-1 lg:overflow-y-auto lg:border-r-3 lg:border-b-0 lg:p-3 xl:w-72"
        >
          {menu.categories.map((c) => {
            const on = c.id === active?.id;
            return (
              <button
                key={c.id}
                type="button"
                aria-current={on ? "true" : undefined}
                onClick={() => setCategory(c.id)}
                className={`press shrink-0 border-l-6 px-4 py-3 text-left font-[family-name:var(--font-display)] text-[length:var(--text-step-1)] whitespace-nowrap lg:w-full ${
                  on ? "border-brand bg-accent text-ink" : "border-transparent text-ink-soft hover:bg-paper-deep"
                }`}
              >
                {c.name}
              </button>
            );
          })}
        </nav>

        <section className="grid flex-1 auto-rows-min grid-cols-2 gap-3 overflow-y-auto p-3 sm:grid-cols-3 sm:gap-4 sm:p-4 2xl:grid-cols-4">
          {active?.products.map((p) => (
            <button
              key={p.id}
              type="button"
              disabled={p.isSoldOut}
              onClick={() => customize(p.id)}
              className="press notch relative flex flex-col border-3 border-line bg-card p-3 text-left disabled:opacity-45 sm:p-4"
            >
              <ProductImage name={p.name} category={active.name} media={p.media} className="h-24 w-full sm:h-32" />
              <span className="mt-2 font-[family-name:var(--font-display)] text-[length:var(--text-step-1)] leading-tight">
                {p.name}
              </span>
              {p.description && (
                <span className="mt-1 text-[length:var(--text-step-0)] leading-snug text-ink-soft">{p.description}</span>
              )}
              <Price value={p.price} className="mt-3 self-start" />

              {/* Sold-out items stay in place, greyed, so nothing jumps around mid-order (docs §12). */}
              {p.isSoldOut && (
                <span className="absolute inset-x-0 top-1/3 -rotate-6 bg-ink py-2 text-center font-[family-name:var(--font-display)] text-[length:var(--text-step-1)] text-paper">
                  Sold out
                </span>
              )}
            </button>
          ))}
        </section>
      </div>
    </Screen>
  );
}
