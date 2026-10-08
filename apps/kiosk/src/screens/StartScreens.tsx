import { useState } from "react";
import { getTableStatus, verifyDeviceToken } from "../api";
import { Button, Modal, Screen, Shape } from "../components/ui";
import { cleanToken, config, savedTokenHint, saveDeviceToken } from "../config";
import { useKiosk } from "../store";

export function AttractScreen() {
  const go = useKiosk((s) => s.go);
  return (
    <button type="button" className="flex h-full w-full flex-col items-center justify-center gap-12" onClick={() => go("dining")}>
      <div className="flex w-full max-w-2xl items-end justify-center gap-8">
        <Shape kind="rice meals" className="w-40!" />
        <Shape kind="drinks" className="w-24!" />
        <Shape kind="desserts" className="w-32!" />
      </div>
      <h1 className="text-7xl font-black uppercase">Order here</h1>
      <p className="animate-pulse border-4 border-black px-10 py-6 text-3xl font-bold uppercase">Touch to start</p>
    </button>
  );
}

export function DiningScreen() {
  const { chooseDining, reset } = useKiosk();
  return (
    <Screen title="Where will you eat?" onBack={reset}>
      <div className="grid h-full grid-cols-2 gap-8 p-10">
        <Button size="xl" onClick={() => chooseDining("DineIn")} className="flex flex-col items-center justify-center gap-6">
          <span className="aspect-square w-32 rounded-full border-8 border-black" />
          Dine in
        </Button>
        <Button size="xl" onClick={() => chooseDining("TakeOut")} className="flex flex-col items-center justify-center gap-6">
          <span className="aspect-square w-28 border-8 border-black" />
          Take out
        </Button>
      </div>
    </Screen>
  );
}

export function ServiceScreen() {
  const { chooseService, go } = useKiosk();
  return (
    <Screen title="Dine in" onBack={() => go("dining")}>
      <div className="grid h-full grid-cols-2 gap-8 p-10">
        <Button size="xl" onClick={() => chooseService("CounterPickup")} className="flex flex-col items-center justify-center gap-4">
          Pick up at counter
          <span className="text-xl normal-case">We'll call your number</span>
        </Button>
        <Button size="xl" onClick={() => chooseService("ServeToTable")} className="flex flex-col items-center justify-center gap-4">
          Serve to my table
          <span className="text-xl normal-case">Grab a number stand first</span>
        </Button>
      </div>
    </Screen>
  );
}

export function TableScreen() {
  const { setTable, go, tableNumber } = useKiosk();
  const [digits, setDigits] = useState(tableNumber?.toString() ?? "");
  const [checking, setChecking] = useState(false);
  const [inUseWarning, setInUseWarning] = useState(false);

  const value = Number(digits);
  const valid = digits !== "" && value >= config.tableMin && value <= config.tableMax;
  const press = (d: string) => setDigits((cur) => (cur.length >= 2 ? cur : (cur + d).replace(/^0+/, "")));

  const confirm = async () => {
    setChecking(true);
    try {
      // Soft warning only: stands get reused, so never block.
      const status = await getTableStatus(value);
      if (status.inUse) setInUseWarning(true);
      else setTable(value);
    } catch {
      setTable(value); // the check is a nicety; don't block ordering on it
    } finally {
      setChecking(false);
    }
  };

  return (
    <Screen title="Your table number" onBack={() => go("service")}>
      <div className="flex h-full flex-col items-center justify-center gap-6 p-6">
        <p className="text-center text-2xl">
          Take a number stand from the counter and enter its number ({config.tableMin}–{config.tableMax}).
        </p>
        <div className="flex h-32 w-64 items-center justify-center border-8 border-black text-8xl font-black tabular-nums">
          {digits || "–"}
        </div>
        <div className="grid w-96 grid-cols-3 gap-3">
          {["1", "2", "3", "4", "5", "6", "7", "8", "9"].map((d) => (
            <Button key={d} size="lg" onClick={() => press(d)}>
              {d}
            </Button>
          ))}
          <Button size="lg" onClick={() => setDigits("")}>
            Clear
          </Button>
          <Button size="lg" onClick={() => press("0")}>
            0
          </Button>
          <Button size="lg" onClick={() => setDigits((cur) => cur.slice(0, -1))} aria-label="Delete">
            ⌫
          </Button>
        </div>
        <Button variant="solid" size="lg" className="w-96" disabled={!valid || checking} onClick={confirm}>
          {checking ? "Checking…" : "Confirm"}
        </Button>
      </div>

      {inUseWarning && (
        <Modal>
          <p className="text-3xl font-black">Table {value} is already in use.</p>
          <p className="mt-4 text-xl">Continue with this number?</p>
          <div className="mt-8 grid grid-cols-2 gap-4">
            <Button size="lg" onClick={() => setInUseWarning(false)}>
              Change
            </Button>
            <Button variant="solid" size="lg" onClick={() => setTable(value)}>
              Continue
            </Button>
          </div>
        </Modal>
      )}
    </Screen>
  );
}

const setupMessages = {
  rejected: "That token wasn't accepted. Copy the whole value, starting with dev_, and try again.",
  unreachable: "Can't reach the server. If it was asleep it can take about a minute to wake up. Try again.",
};

/**
 * First run, or when the API rejects the saved token. The token is checked with the API before it is saved,
 * so a typo shows up here instead of after the customer touches Start.
 */
export function SetupScreen({ error }: { error?: string }) {
  const [token, setToken] = useState("");
  const [status, setStatus] = useState<"idle" | "checking" | "rejected" | "unreachable">("idle");
  const candidate = cleanToken(token);
  const hint = savedTokenHint();

  const save = async () => {
    setStatus("checking");
    const result = await verifyDeviceToken(candidate);
    if (result === "ok") {
      saveDeviceToken(candidate);
      location.reload();
    } else {
      setStatus(result);
    }
  };

  const message =
    status === "rejected" || status === "unreachable"
      ? setupMessages[status]
      : error && (hint ? `${error} The saved token ends in …${hint}.` : error);

  return (
    <Screen title="Kiosk setup">
      <div className="mx-auto flex max-w-xl flex-col gap-6 p-10">
        <p className="text-xl">Enter the device token shown when this kiosk was registered in the Admin app.</p>
        {message && <p className="border-4 border-black p-4 text-lg font-bold">{message}</p>}
        <input
          className="min-h-16 border-4 border-black px-4 text-xl"
          value={token}
          onChange={(e) => {
            setToken(e.target.value);
            setStatus("idle");
          }}
          placeholder="dev_…"
          autoFocus
        />
        <Button
          variant="solid"
          size="lg"
          disabled={!candidate.startsWith("dev_") || candidate.length < 36 || status === "checking"}
          onClick={save}
        >
          {status === "checking" ? "Checking…" : "Save"}
        </Button>
      </div>
    </Screen>
  );
}
