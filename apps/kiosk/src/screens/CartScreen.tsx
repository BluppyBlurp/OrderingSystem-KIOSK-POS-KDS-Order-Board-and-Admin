import type { Menu } from "../api";
import { Button, delta, money, Screen } from "../components/ui";
import { cartTotal, lineTotal, MAX_QUANTITY } from "../lib/cart";
import { suggestUpsells } from "../lib/upsell";
import { useKiosk } from "../store";
import { OrderSummaryBadge } from "./MenuScreen";

export function CartScreen({ menu }: { menu: Menu }) {
  const { cart, setLineQuantity, go, checkout } = useKiosk();

  return (
    <Screen
      title="Your tray"
      onBack={() => go("menu")}
      badge={<OrderSummaryBadge />}
      footer={
        <div className="flex items-center gap-3">
          <Button size="lg" onClick={() => go("menu")}>
            Add more
          </Button>
          <Button
            variant="primary"
            size="lg"
            className="notch-sm ml-auto flex-1 sm:flex-none sm:min-w-80"
            disabled={cart.length === 0}
            onClick={() => checkout(suggestUpsells(menu, cart).length > 0)}
          >
            Pay · {money(cartTotal(cart))}
          </Button>
        </div>
      }
    >
      {cart.length === 0 ? (
        <div className="flex h-full flex-col items-center justify-center gap-5 p-8 text-center">
          <p className="font-[family-name:var(--font-display)] text-[length:var(--text-step-3)]">Nothing here yet</p>
          <Button variant="primary" size="lg" className="notch-sm" onClick={() => go("menu")}>
            Browse the menu
          </Button>
        </div>
      ) : (
        <>
          <ul className="divide-y-3 divide-line border-b-3 border-line">
            {cart.map((line) => (
              <li key={line.key} className="flex flex-wrap items-center gap-x-4 gap-y-3 bg-card p-4 sm:p-5">
                <div className="min-w-48 flex-1">
                  <p className="font-[family-name:var(--font-display)] text-[length:var(--text-step-1)] leading-tight">
                    {line.name}
                  </p>
                  {line.modifiers.map((m) => (
                    <p key={m.id} className="text-[length:var(--text-step-0)] text-ink-soft">
                      {m.name} {delta(m.priceDelta)}
                    </p>
                  ))}
                </div>

                <div className="flex items-center gap-2">
                  <Button
                    className="w-12"
                    onClick={() => setLineQuantity(line.key, line.quantity - 1)}
                    aria-label={line.quantity === 1 ? `Remove ${line.name}` : "One less"}
                  >
                    {line.quantity === 1 ? "✕" : "−"}
                  </Button>
                  <span className="w-10 text-center font-[family-name:var(--font-display)] text-[length:var(--text-step-2)] tabular-nums">
                    {line.quantity}
                  </span>
                  <Button
                    className="w-12"
                    disabled={line.quantity >= MAX_QUANTITY}
                    onClick={() => setLineQuantity(line.key, line.quantity + 1)}
                    aria-label="One more"
                  >
                    +
                  </Button>
                </div>

                <p className="w-28 text-right font-[family-name:var(--font-display)] text-[length:var(--text-step-1)]">
                  {money(lineTotal(line))}
                </p>
              </li>
            ))}
          </ul>

          <div className="flex items-center justify-between gap-4 p-5 sm:p-6">
            <span className="font-[family-name:var(--font-display)] text-[length:var(--text-step-2)]">Total</span>
            <span className="font-[family-name:var(--font-display)] text-[length:var(--text-step-3)]">
              {money(cartTotal(cart))}
            </span>
          </div>
        </>
      )}
    </Screen>
  );
}
