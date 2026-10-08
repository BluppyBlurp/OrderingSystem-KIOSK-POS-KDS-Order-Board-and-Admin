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
      title="Your order"
      onBack={() => go("menu")}
      footer={
        <div className="flex items-center justify-between gap-4">
          <Button size="lg" onClick={() => go("menu")}>
            Add more
          </Button>
          <OrderSummaryBadge />
          <Button variant="solid" size="lg" className="min-w-80" disabled={cart.length === 0} onClick={() => checkout(suggestUpsells(menu, cart).length > 0)}>
            Checkout · {money(cartTotal(cart))}
          </Button>
        </div>
      }
    >
      {cart.length === 0 ? (
        <p className="p-10 text-center text-3xl">Your cart is empty.</p>
      ) : (
        <ul className="divide-y-4 divide-black">
          {cart.map((line) => (
            <li key={line.key} className="flex items-center gap-6 p-6">
              <div className="flex-1">
                <p className="text-2xl font-black">{line.name}</p>
                {line.modifiers.map((m) => (
                  <p key={m.id} className="text-lg">
                    – {m.name} {delta(m.priceDelta)}
                  </p>
                ))}
              </div>
              <div className="flex items-center gap-3">
                <Button
                  className="w-14"
                  onClick={() => setLineQuantity(line.key, line.quantity - 1)}
                  aria-label={line.quantity === 1 ? "Remove" : "Less"}
                >
                  {line.quantity === 1 ? "✕" : "−"}
                </Button>
                <span className="w-12 text-center text-3xl font-black tabular-nums">{line.quantity}</span>
                <Button
                  className="w-14"
                  disabled={line.quantity >= MAX_QUANTITY}
                  onClick={() => setLineQuantity(line.key, line.quantity + 1)}
                  aria-label="More"
                >
                  +
                </Button>
              </div>
              <p className="w-36 text-right text-2xl font-black">{money(lineTotal(line))}</p>
            </li>
          ))}
        </ul>
      )}
    </Screen>
  );
}
