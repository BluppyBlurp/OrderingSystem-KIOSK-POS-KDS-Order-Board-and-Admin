import { ApiError } from "@kiosk/api-client";
import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { BOARD_KEY, useBoard, verifyToken, type BoardEntry } from "./api";
import { Button } from "./components/ui";
import { cleanToken, clearDeviceToken, getDeviceToken, saveDeviceToken } from "./config";
import { beep, soundEnabled, unlockSound } from "./lib/beep";
import { newlyReady, shortNumber } from "./lib/board";
import { useBoardHub } from "./realtime";

const HIGHLIGHT_MS = 15_000;

export function App() {
  if (!getDeviceToken()) return <SetupScreen />;
  return <BoardScreen />;
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

  return (
    <div className="flex h-full flex-col">
      <div className="grid min-h-0 flex-1 grid-cols-2">
        <Column title="Preparing" entries={board.data?.preparing ?? []} highlighted={new Set()} />
        <Column title="Now serving" entries={board.data?.ready ?? []} highlighted={highlighted} inverted />
      </div>
      <footer className="flex min-h-12 items-center justify-between border-t-4 border-black px-4 text-lg font-bold">
        <span>{hub === "live" ? "" : hub === "connecting" ? "Reconnecting…" : "Offline — retrying"}</span>
        {board.isError && !(board.error instanceof ApiError) && <span>Can't reach the server — showing the last update</span>}
        {!sound && (
          <Button
            onClick={() => {
              setSound(unlockSound());
              beep();
            }}
          >
            Tap to enable chime
          </Button>
        )}
      </footer>
    </div>
  );
}

function Column({
  title,
  entries,
  highlighted,
  inverted = false,
}: {
  title: string;
  entries: BoardEntry[];
  highlighted: Set<string>;
  inverted?: boolean;
}) {
  return (
    <section className={`flex min-h-0 flex-col ${inverted ? "bg-black text-white" : "border-r-4 border-black"}`}>
      <h1 className={`border-b-4 p-6 text-center text-6xl font-black uppercase ${inverted ? "border-white" : "border-black"}`}>{title}</h1>
      <div className="grid flex-1 auto-rows-min grid-cols-2 gap-6 overflow-hidden p-6">
        {entries.map((e) => (
          <div
            key={e.orderNumber}
            className={`flex flex-col items-center p-2 ${highlighted.has(e.orderNumber) ? "animate-pulse outline-8 outline-white" : ""}`}
          >
            <span className="text-8xl font-black tabular-nums">{shortNumber(e.orderNumber)}</span>
            {e.type === "ServeToTable" && <span className="text-3xl font-bold">Table {e.tableNumber}</span>}
          </div>
        ))}
      </div>
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

  return (
    <div className="flex h-full flex-col items-center justify-center gap-6 p-10 text-center">
      <h1 className="text-5xl font-black uppercase">Order board setup</h1>
      <p className="max-w-2xl text-xl">
        In the Admin app, go to Devices, register a <b>Board</b>, and paste the token it shows (it starts with <code>dev_</code>).
      </p>
      {error && <p className="border-4 border-black p-3 text-xl font-bold">{error}</p>}
      <input
        className="w-full max-w-2xl border-4 border-black p-4 text-2xl"
        placeholder="dev_…"
        value={token}
        onChange={(e) => setToken(e.target.value)}
      />
      <div className="flex gap-4">
        <Button variant="solid" size="lg" disabled={!cleanToken(token) || status === "checking"} onClick={save}>
          {status === "checking" ? "Checking…" : "Save"}
        </Button>
        {error && (
          <Button size="lg" onClick={() => (clearDeviceToken(), location.reload())}>
            Clear saved token
          </Button>
        )}
      </div>
      {status === "rejected" && <p className="text-xl font-bold">That token was not accepted. Check it and try again.</p>}
      {status === "unreachable" && <p className="text-xl font-bold">Couldn't reach the server. Check the connection and try again.</p>}
    </div>
  );
}
