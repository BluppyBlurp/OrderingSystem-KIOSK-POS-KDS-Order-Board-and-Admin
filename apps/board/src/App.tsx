import { ApiError } from "@kiosk/api-client";
import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { BOARD_KEY, useBoard, verifyToken, type BoardEntry } from "./api";
import { BRAND } from "./brand";
import { Button, Mark } from "./components/ui";
import { cleanToken, clearDeviceToken, getDeviceToken, saveDeviceToken } from "./config";
import { beep, soundEnabled, unlockSound } from "./lib/beep";
import { newlyReady, shortNumber } from "./lib/board";
import { useBoardHub } from "./realtime";

const HIGHLIGHT_MS = 15_000;

export function App() {
  if (!getDeviceToken()) return <SetupScreen />;
  return <BoardScreen />;
}

/** A wall clock on the board saves customers asking how long they have been waiting. */
function Clock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 15_000);
    return () => clearInterval(t);
  }, []);
  return (
    <span className="display text-[length:var(--text-step-2)] tabular-nums">
      {now.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
    </span>
  );
}

function BoardScreen() {
  const queryClient = useQueryClient();
  const board = useBoard();
  const refresh = useCallback(() => void queryClient.invalidateQueries({ queryKey: BOARD_KEY }), [queryClient]);
  const hub = useBoardHub(refresh);
  const [sound, setSound] = useState(soundEnabled);
  const [highlighted, setHighlighted] = useState<Set<string>>(new Set());
  const seenReady = useRef<Set<string> | null>(null);

  // Chime + highlight when a number moves to "Now serving" (works whether a push or a poll brought it in).
  useEffect(() => {
    if (!board.data) return;
    const arrived = newlyReady(seenReady.current, board.data.ready);
    seenReady.current = new Set(board.data.ready.map((e) => e.orderNumber));
    if (arrived.length === 0) return;
    beep([660, 880, 1320]);
    setHighlighted((h) => new Set([...h, ...arrived]));
    setTimeout(() => setHighlighted((h) => new Set([...h].filter((n) => !arrived.includes(n)))), HIGHLIGHT_MS);
  }, [board.data]);

  if (board.error instanceof ApiError && (board.error.status === 401 || board.error.status === 403)) {
    return <SetupScreen error="This board's token was not accepted (revoked or mistyped). Enter a new one." />;
  }

  const trouble =
    hub === "connecting"
      ? "Reconnecting…"
      : hub === "offline"
        ? "Offline — retrying"
        : board.isError && !(board.error instanceof ApiError)
          ? "Can't reach the server — showing the last update"
          : null;

  return (
    <div className="flex h-full flex-col bg-paper">
      <header className="flex items-center gap-3 bg-brand px-5 py-2 text-paper">
        <Mark className="h-7 w-7 shrink-0" />
        <span className="display text-[length:var(--text-step-2)]">{BRAND.name}</span>
        <div className="ml-auto flex items-center gap-5">
          {trouble && <span className="text-[length:var(--text-step-0)] text-paper/85">{trouble}</span>}
          {!sound && (
            <Button
              onClick={() => {
                setSound(unlockSound());
                beep();
              }}
            >
              Turn on the chime
            </Button>
          )}
          <Clock />
        </div>
      </header>

      {/* A board is hung either way: side by side in landscape, stacked in portrait. */}
      <div className="grid min-h-0 flex-1 grid-rows-2 landscape:grid-cols-2 landscape:grid-rows-1">
        <Column
          title="Preparing"
          empty="Nothing cooking"
          entries={board.data?.preparing ?? []}
          highlighted={new Set()}
        />
        <Column
          title="Ready — collect at the counter"
          shortTitle="Ready"
          empty="Nothing ready yet"
          entries={board.data?.ready ?? []}
          highlighted={highlighted}
          loud
        />
      </div>
    </div>
  );
}

/**
 * One side of the board. "Ready" is the loud one: amber is where the system says look here, and a
 * standing customer should be able to find their number without reading the heading first.
 */
function Column({
  title,
  shortTitle,
  empty,
  entries,
  highlighted,
  loud = false,
}: {
  title: string;
  shortTitle?: string;
  empty: string;
  entries: BoardEntry[];
  highlighted: Set<string>;
  loud?: boolean;
}) {
  return (
    <section
      className={`flex min-h-0 flex-col border-b-6 border-line last:border-b-0 landscape:border-b-0 landscape:border-r-6 landscape:last:border-r-0 ${
        loud ? "bg-accent" : "bg-paper"
      }`}
    >
      <h1
        className={`border-b-3 border-line px-5 py-3 text-center display text-[length:var(--text-step-3)] ${
          loud ? "text-ink" : "text-ink-soft"
        }`}
      >
        <span className="hidden sm:inline">{title}</span>
        <span className="sm:hidden">{shortTitle ?? title}</span>
      </h1>

      {entries.length === 0 ? (
        <p className="flex flex-1 items-center justify-center p-6 text-center text-[length:var(--text-step-2)] text-ink-soft">
          {empty}
        </p>
      ) : (
        <div
          className={`numbers grid flex-1 auto-rows-min content-start gap-x-4 gap-y-2 overflow-hidden p-4 ${
            loud ? "grid-cols-2" : "grid-cols-2 landscape:xl:grid-cols-3"
          }`}
        >
          {entries.map((e) => (
            <div
              key={e.orderNumber}
              className={`flex min-w-0 flex-col items-center ${highlighted.has(e.orderNumber) ? "arrive" : ""}`}
            >
              <span className={`display leading-none tabular-nums ${loud ? "number-loud" : "number-quiet text-ink-soft"}`}>
                {shortNumber(e.orderNumber)}
              </span>
              {e.type === "ServeToTable" && (
                <span className="text-[length:var(--text-step-1)] font-semibold">Table {e.tableNumber}</span>
              )}
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

function SetupScreen({ error }: { error?: string }) {
  const [token, setToken] = useState("");
  const [status, setStatus] = useState<"idle" | "checking" | "rejected" | "unreachable">("idle");

  const save = async () => {
    const candidate = cleanToken(token);
    setStatus("checking");
    const result = await verifyToken(candidate);
    if (result === "ok") {
      saveDeviceToken(candidate);
      location.reload();
    } else {
      setStatus(result);
    }
  };

  const message =
    status === "rejected"
      ? "That token wasn't accepted. Copy the whole value and try again."
      : status === "unreachable"
        ? "Can't reach the server. If it was asleep it can take about a minute to wake up."
        : error;

  return (
    <div className="flex h-full flex-col bg-paper">
      <header className="flex items-center gap-3 bg-brand px-5 py-2 text-paper">
        <Mark className="h-7 w-7 shrink-0" />
        <span className="display text-[length:var(--text-step-2)]">{BRAND.name}</span>
      </header>

      <div className="mx-auto flex w-full max-w-2xl flex-1 flex-col justify-center gap-5 p-8">
        <h1 className="display text-[length:var(--text-step-4)]">Set up this board</h1>
        <p className="text-[length:var(--text-step-1)] text-ink-soft">
          In Admin, open Devices, register a <b>Board</b>, and paste the token it shows. It starts with{" "}
          <code className="bg-paper-deep px-1">dev_</code>.
        </p>
        {message && (
          <p className="border-3 border-brand bg-card p-4 text-[length:var(--text-step-0)] font-semibold text-brand">
            {message}
          </p>
        )}
        <input
          className="min-h-14 border-3 border-line bg-card px-4 text-[length:var(--text-step-1)] outline-none focus:border-brand"
          placeholder="dev_…"
          value={token}
          onChange={(e) => {
            setToken(e.target.value);
            setStatus("idle");
          }}
          autoFocus
        />
        <div className="flex gap-3">
          <Button
            variant="primary"
            size="lg"
            className="notch-sm flex-1"
            disabled={!cleanToken(token) || status === "checking"}
            onClick={save}
          >
            {status === "checking" ? "Checking…" : "Save and start"}
          </Button>
          {error && (
            <Button size="lg" onClick={() => (clearDeviceToken(), location.reload())}>
              Clear saved token
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}
