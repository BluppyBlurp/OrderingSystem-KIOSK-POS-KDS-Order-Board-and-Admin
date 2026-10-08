import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import {
  createGroup,
  createModifier,
  deleteGroup,
  deleteModifier,
  errorText,
  keys,
  updateGroup,
  updateModifier,
  useModifierGroups,
  type Modifier,
  type ModifierGroup,
} from "../api";
import { Check, ErrorBox, Field, parseNumber, TextInput } from "../components/form";
import { Button, money } from "../components/ui";

/**
 * Option groups are the kiosk's questions ("Choose your drink", "Upsize your fries?"). A product lists which groups
 * it asks, in order, in its own editor. Option names should describe themselves ("Large fries", not "Large"),
 * because receipts and kitchen tickets show them without the question.
 */
export function ModifiersPage() {
  const queryClient = useQueryClient();
  const groups = useModifierGroups();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const list = groups.data ?? [];
  const selected = selectedId === "new" ? null : (list.find((g) => g.id === selectedId) ?? list[0] ?? null);

  const run = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      await action();
    } catch (e) {
      setError(errorText(e));
    } finally {
      await queryClient.invalidateQueries({ queryKey: keys.groups });
    }
  };

  return (
    <div className="flex h-full flex-col">
      <div className="p-4 pb-0">
        <ErrorBox message={error} onDismiss={() => setError(null)} />
      </div>
      <div className="flex min-h-0 flex-1">
        <aside className="flex w-96 shrink-0 flex-col gap-2 overflow-y-auto border-r-4 border-black p-4">
          <h2 className="text-xl font-black uppercase">Option groups</h2>
          {list.map((g) => (
            <Button
              key={g.id}
              variant={g.id === selected?.id ? "solid" : "outline"}
              className="text-left normal-case"
              onClick={() => setSelectedId(g.id)}
            >
              {g.name}
            </Button>
          ))}
          <Button variant={selectedId === "new" ? "solid" : "outline"} onClick={() => setSelectedId("new")}>
            + New group
          </Button>
        </aside>
        <section className="min-w-0 flex-1 overflow-y-auto p-4">
          {selectedId === "new" ? (
            <GroupForm
              key="new"
              group={null}
              onSave={(body) =>
                run(async () => {
                  const created = await createGroup(body);
                  setSelectedId(created.id);
                })
              }
            />
          ) : selected ? (
            <>
              <GroupForm
                key={selected.id}
                group={selected}
                onSave={(body) => run(() => updateGroup(selected.id, body))}
                onDelete={() => confirm(`Delete "${selected.name}" and its options?`) && run(() => deleteGroup(selected.id))}
              />
              <ModifierTable group={selected} run={run} />
            </>
          ) : (
            !groups.isLoading && <p className="text-xl">No option groups yet.</p>
          )}
        </section>
      </div>
    </div>
  );
}

type GroupBody = { name: string; minSelect: number; maxSelect: number; isRequired: boolean };

function GroupForm({ group, onSave, onDelete }: { group: ModifierGroup | null; onSave: (b: GroupBody) => void; onDelete?: () => void }) {
  const [name, setName] = useState(group?.name ?? "");
  const [min, setMin] = useState(String(group?.minSelect ?? 0));
  const [max, setMax] = useState(String(group?.maxSelect ?? 1));
  const [isRequired, setIsRequired] = useState(group?.isRequired ?? false);

  return (
    <form
      className="mb-6 flex flex-col gap-3 border-4 border-black p-4"
      onSubmit={(e) => {
        e.preventDefault();
        onSave({ name: name.trim(), minSelect: parseNumber(min) ?? 0, maxSelect: parseNumber(max) ?? 1, isRequired });
      }}
    >
      <h2 className="text-2xl font-black uppercase">{group ? "Question" : "New question"}</h2>
      <Field label="Question shown on the kiosk">
        <TextInput value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Choose your drink" />
      </Field>
      <div className="grid grid-cols-3 items-end gap-3">
        <Field label="Pick at least">
          <TextInput inputMode="numeric" value={min} onChange={(e) => setMin(e.target.value)} />
        </Field>
        <Field label="Pick at most">
          <TextInput inputMode="numeric" value={max} onChange={(e) => setMax(e.target.value)} />
        </Field>
        <Check label="Required" checked={isRequired} onChange={setIsRequired} />
      </div>
      <div className="flex gap-3">
        <Button type="submit" variant="solid" disabled={!name.trim()}>
          {group ? "Save question" : "Create question"}
        </Button>
        {onDelete && (
          <Button type="button" onClick={onDelete}>
            Delete group
          </Button>
        )}
      </div>
    </form>
  );
}

function ModifierTable({ group, run }: { group: ModifierGroup; run: (action: () => Promise<unknown>) => Promise<void> }) {
  return (
    <div className="border-4 border-black p-4">
      <h3 className="mb-2 text-xl font-black uppercase">Options</h3>
      <table className="w-full text-left text-lg">
        <thead>
          <tr className="border-b-4 border-black">
            <th className="p-2">Name</th>
            <th className="p-2">Price change (₱)</th>
            <th className="p-2">Order</th>
            <th className="p-2">Available</th>
            <th className="p-2" />
          </tr>
        </thead>
        <tbody>
          {group.modifiers.map((m) => (
            <ModifierRow
              key={m.id}
              modifier={m}
              onSave={(body) => run(() => updateModifier(group.id, m.id, body))}
              onDelete={() => run(() => deleteModifier(group.id, m.id))}
            />
          ))}
          <ModifierRow
            key={`new-${group.modifiers.length}`}
            modifier={null}
            nextSort={group.modifiers.length}
            onSave={(body) => run(() => createModifier(group.id, body))}
          />
        </tbody>
      </table>
    </div>
  );
}

type ModifierBody = { name: string; priceDelta: number; isAvailable: boolean; sortOrder: number };

function ModifierRow({
  modifier,
  nextSort = 0,
  onSave,
  onDelete,
}: {
  modifier: Modifier | null;
  nextSort?: number;
  onSave: (b: ModifierBody) => void;
  onDelete?: () => void;
}) {
  const [name, setName] = useState(modifier?.name ?? "");
  const [delta, setDelta] = useState(String(modifier?.priceDelta ?? 0));
  const [sort, setSort] = useState(String(modifier?.sortOrder ?? nextSort));
  const [isAvailable, setIsAvailable] = useState(modifier?.isAvailable ?? true);
  const body = () => ({ name: name.trim(), priceDelta: parseNumber(delta) ?? 0, sortOrder: parseNumber(sort) ?? 0, isAvailable });

  return (
    <tr className="border-b-2 border-black">
      <td className="p-2">
        <TextInput value={name} onChange={(e) => setName(e.target.value)} placeholder={modifier ? "" : "New option, e.g. Large fries"} aria-label="Option name" />
      </td>
      <td className="w-40 p-2">
        <TextInput inputMode="decimal" value={delta} onChange={(e) => setDelta(e.target.value)} aria-label="Price change" />
      </td>
      <td className="w-24 p-2">
        <TextInput inputMode="numeric" value={sort} onChange={(e) => setSort(e.target.value)} aria-label="Sort order" />
      </td>
      <td className="p-2">
        <Check label="" checked={isAvailable} onChange={setIsAvailable} />
      </td>
      <td className="p-2 text-right whitespace-nowrap">
        {modifier && <span className="mr-2 text-sm">{money(modifier.priceDelta)}</span>}
        <Button
          variant="solid"
          disabled={!name.trim()}
          onClick={() => {
            onSave(body());
            if (!modifier) setName("");
          }}
        >
          {modifier ? "Save" : "Add"}
        </Button>{" "}
        {onDelete && <Button onClick={onDelete}>Delete</Button>}
      </td>
    </tr>
  );
}
