import { ApiError } from "@kiosk/api-client";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { useMenu } from "./api";
import { Button } from "./components/ui";
import { getDeviceToken } from "./config";
import { useIdleReset } from "./hooks/useIdleReset";
import { useKioskHub } from "./realtime";
import { CartScreen } from "./screens/CartScreen";
import { CheckoutScreen } from "./screens/CheckoutScreen";
import { CustomizeScreen } from "./screens/CustomizeScreen";
import { MenuScreen } from "./screens/MenuScreen";
import { CashSlipScreen, OnlinePayScreen, ReceiptScreen } from "./screens/PaymentScreens";
import { UpsellScreen } from "./screens/UpsellScreen";
import { AttractScreen, DiningScreen, ServiceScreen, SetupScreen, TableScreen } from "./screens/StartScreens";
import { useKiosk, type Screen } from "./store";

/** Screens where a walk-away customer's cart should be cleared. Not during payment or on the slip/receipt. */
const IDLE_SCREENS: Screen[] = ["dining", "service", "table", "menu", "customize", "cart", "upsell", "checkout"];

export function App() {
  const { screen, reset } = useKiosk();
  const menu = useMenu();
  const queryClient = useQueryClient();
  const idleModal = useIdleReset(IDLE_SCREENS.includes(screen), reset);
  useKioskHub(); // MenuChanged updates prices and sold-out flags mid-order; order pushes speed up the payment screen

  // Each new customer also starts from a fresh menu, in case a MenuChanged push was missed.
  useEffect(() => {
    if (screen === "attract") void queryClient.invalidateQueries({ queryKey: ["menu"] });
  }, [screen, queryClient]);

  if (!getDeviceToken()) return <SetupScreen />;

  if (menu.error instanceof ApiError && menu.error.status === 401) {
    return <SetupScreen error="This kiosk's token was not accepted (revoked or mistyped). Enter a new one." />;
  }

  if (screen === "attract") return <AttractScreen />;

  if (!menu.data) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-6 bg-paper p-8 text-center">
        {menu.isError ? (
          <>
            <p className="font-[family-name:var(--font-display)] text-[length:var(--text-step-3)]">
              The menu isn't loading
            </p>
            <p className="text-[length:var(--text-step-1)] text-ink-soft">Check the connection, then try again.</p>
            <Button variant="primary" size="lg" className="notch-sm" onClick={() => menu.refetch()}>
              Try again
            </Button>
          </>
        ) : (
          <p className="animate-pulse font-[family-name:var(--font-display)] text-[length:var(--text-step-2)]">
            Loading the menu…
          </p>
        )}
      </div>
    );
  }

  return (
    <>
      {screen === "dining" && <DiningScreen />}
      {screen === "service" && <ServiceScreen />}
      {screen === "table" && <TableScreen />}
      {screen === "menu" && <MenuScreen menu={menu.data} />}
      {screen === "customize" && <CustomizeScreen menu={menu.data} />}
      {screen === "cart" && <CartScreen menu={menu.data} />}
      {screen === "upsell" && <UpsellScreen menu={menu.data} />}
      {screen === "checkout" && <CheckoutScreen />}
      {screen === "cashSlip" && <CashSlipScreen />}
      {screen === "onlinePay" && <OnlinePayScreen />}
      {screen === "receipt" && <ReceiptScreen />}
      {idleModal}
    </>
  );
}
