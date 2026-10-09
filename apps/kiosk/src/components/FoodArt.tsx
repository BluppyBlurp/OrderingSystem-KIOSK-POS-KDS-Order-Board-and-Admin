/**
 * Built-in food artwork.
 *
 * Every product gets a picture even before anyone uploads a photo in Admin: flat vector drawings in
 * the house palette, matched to the product by name and falling back to the category. They are SVG
 * rather than image files, so they stay sharp on a 1080x1920 kiosk panel, need no hosting, and work
 * with the network down. An uploaded image always wins — see ProductImage.
 */
import type { ReactNode } from "react";

const INK = "#241612";

/** Food colours, chosen to sit beside the chili red and amber without fighting them. */
const C = {
  rice: "#FFFBF2",
  chicken: "#E8A33D",
  chickenDeep: "#C97B22",
  bun: "#E8B562",
  bunDeep: "#CF9540",
  patty: "#7B4A2D",
  gravy: "#8A5A2B",
  lettuce: "#6FA84B",
  sauce: "#C4321F",
  cheese: "#F5B700",
  cream: "#F2E2C4",
  cola: "#4A2318",
  lime: "#8FC63D",
  tea: "#B5651D",
  pineapple: "#F5C518",
  water: "#9ED8E8",
  choco: "#5A3620",
  plate: "#FFFFFF",
  card: "#FFF4DE",
} as const;

const stroke = { stroke: INK, strokeWidth: 3, strokeLinejoin: "round" as const, strokeLinecap: "round" as const };
const thin = { stroke: INK, strokeWidth: 2, strokeLinecap: "round" as const };

/* ---------------------------------------------------------------- primitives */

/** A drinks cup with lid and straw. Fill is the drink colour. */
function Cup({ fill, x = 0, y = 0, s = 1 }: { fill: string; x?: number; y?: number; s?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${s})`}>
      <path d="M13 6 L31 6 L28 44 L16 44 Z" fill={fill} {...stroke} />
      <rect x="9" y="1" width="26" height="8" rx="2" fill={C.plate} {...stroke} />
      <path d="M24 1 L29 -12" {...stroke} fill="none" />
      <path d="M15 20 L29 20" {...thin} opacity="0.35" fill="none" />
    </g>
  );
}

/** Carton of fries. */
function Fries({ x = 0, y = 0, s = 1 }: { x?: number; y?: number; s?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${s})`}>
      <path d="M6 10 L13 2 L19 11 L25 1 L31 10 L34 12 L30 42 L10 42 L6 12 Z" fill={C.cheese} {...stroke} />
      <path d="M4 12 L36 12 L32 42 L8 42 Z" fill={C.sauce} {...stroke} />
      <path d="M20 18 L20 36" {...thin} opacity="0.4" fill="none" />
    </g>
  );
}

/** Mound of rice on a small plate. */
function Rice({ x = 0, y = 0, s = 1 }: { x?: number; y?: number; s?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${s})`}>
      <ellipse cx="22" cy="30" rx="22" ry="7" fill={C.plate} {...stroke} />
      <path d="M4 29 Q22 1 40 29 Z" fill={C.rice} {...stroke} />
    </g>
  );
}

/** Fried chicken drumstick. */
function Chicken({ x = 0, y = 0, s = 1, r = 0 }: { x?: number; y?: number; s?: number; r?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${s}) rotate(${r})`}>
      <path d="M30 4 Q46 10 42 26 Q38 42 22 40 Q8 38 10 24 Q12 8 30 4 Z" fill={C.chicken} {...stroke} />
      <path d="M12 36 L2 48 M12 36 L6 50" {...stroke} fill="none" />
      <circle cx="2" cy="50" r="5" fill={C.rice} {...stroke} />
      <path d="M22 14 q6 4 2 10 M32 20 q5 3 2 9" {...thin} opacity="0.5" fill="none" />
    </g>
  );
}

/** Stacked burger: bun, patty, cheese, lettuce. */
function Burger({ x = 0, y = 0, s = 1, cheese = true }: { x?: number; y?: number; s?: number; cheese?: boolean }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${s})`}>
      <path d="M2 20 Q2 0 26 0 Q50 0 50 20 Z" fill={C.bun} {...stroke} />
      <circle cx="16" cy="10" r="1.6" fill={INK} opacity="0.4" />
      <circle cx="28" cy="7" r="1.6" fill={INK} opacity="0.4" />
      <circle cx="38" cy="12" r="1.6" fill={INK} opacity="0.4" />
      <path d="M0 20 L52 20 L52 26 L0 26 Z" fill={C.lettuce} {...stroke} />
      {cheese && <path d="M2 26 L50 26 L44 34 L8 34 Z" fill={C.cheese} {...stroke} />}
      <rect x="1" y={cheese ? 32 : 26} width="50" height="10" rx="4" fill={C.patty} {...stroke} />
      <path d="M2 42 Q2 52 26 52 Q50 52 50 42 Z" fill={C.bunDeep} {...stroke} />
    </g>
  );
}

/** Bowl of noodles. Sauce colour tells spaghetti from carbonara. */
function Noodles({ sauce, x = 0, y = 0, s = 1 }: { sauce: string; x?: number; y?: number; s?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${s})`}>
      <path d="M2 20 Q24 4 46 20 Z" fill={C.rice} {...stroke} />
      <path d="M8 16 Q24 8 40 16 Q32 22 24 18 Q16 22 8 16 Z" fill={sauce} {...stroke} />
      <path d="M2 20 Q4 42 24 42 Q44 42 46 20 Z" fill={C.plate} {...stroke} />
      <path d="M10 28 Q24 34 38 28" {...thin} opacity="0.3" fill="none" />
    </g>
  );
}

/** Small tub with a domed filling — coleslaw, mash, corn. */
function Tub({ fill, dots, x = 0, y = 0, s = 1 }: { fill: string; dots?: string; x?: number; y?: number; s?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${s})`}>
      <path d="M4 14 Q20 2 36 14 Z" fill={fill} {...stroke} />
      <path d="M2 14 L38 14 L34 40 L6 40 Z" fill={C.card} {...stroke} />
      {dots && (
        <g fill={dots}>
          <circle cx="14" cy="10" r="2.2" />
          <circle cx="22" cy="8" r="2.2" />
          <circle cx="28" cy="12" r="2.2" />
        </g>
      )}
    </g>
  );
}

/* ---------------------------------------------------------------- scenes */

/** A meal: the main dish, with a drink and fries behind it. */
function Meal({ main }: { main: ReactNode }) {
  return (
    <>
      <Cup fill={C.cola} x={100} y={28} s={1.1} />
      <Fries x={8} y={34} s={0.95} />
      {main}
    </>
  );
}

const scenes: Record<string, ReactNode> = {
  chickenRice: (
    <>
      <Rice x={70} y={48} s={1.15} />
      <Chicken x={30} y={30} s={1.2} />
    </>
  ),
  chickenRiceMeal: <Meal main={<><Rice x={62} y={52} s={0.9} /><Chicken x={42} y={26} s={1} /></>} />,
  chicken2Meal: <Meal main={<><Rice x={62} y={54} s={0.85} /><Chicken x={36} y={22} s={0.9} /><Chicken x={62} y={30} s={0.9} r={12} /></>} />,
  burgerSteakRice: (
    <>
      <Rice x={74} y={48} s={1.15} />
      <ellipse cx="48" cy="60" rx="34" ry="20" fill={C.gravy} {...stroke} />
      <rect x="24" y="40" width="48" height="18" rx="8" fill={C.patty} {...stroke} />
    </>
  ),
  burgerSteakMeal: (
    <Meal
      main={
        <>
          <Rice x={64} y={54} s={0.85} />
          <ellipse cx="54" cy="58" rx="26" ry="14" fill={C.gravy} {...stroke} />
          <rect x="36" y="42" width="36" height="14" rx="6" fill={C.patty} {...stroke} />
        </>
      }
    />
  ),
  burger: <Burger x={54} y={32} s={1.15} />,
  burgerMeal: <Meal main={<Burger x={50} y={34} s={0.95} />} />,
  chickenSandwich: (
    <g>
      <Burger x={54} y={32} s={1.15} cheese={false} />
      <path d="M62 68 q22 -10 44 0" {...stroke} fill="none" opacity="0.35" />
    </g>
  ),
  spaghetti: <Noodles sauce={C.sauce} x={54} y={38} s={1.3} />,
  spaghettiMeal: <Meal main={<Noodles sauce={C.sauce} x={48} y={40} s={1.05} />} />,
  carbonara: <Noodles sauce={C.cream} x={54} y={38} s={1.3} />,
  fries: <Fries x={58} y={28} s={1.5} />,
  coleslaw: <Tub fill={C.lettuce} dots={C.rice} x={58} y={34} s={1.5} />,
  mash: <Tub fill={C.rice} dots={C.gravy} x={58} y={34} s={1.5} />,
  corn: <Tub fill={C.cheese} dots={C.chickenDeep} x={58} y={34} s={1.5} />,
  cola: <Cup fill={C.cola} x={62} y={32} s={1.6} />,
  lemonLime: <Cup fill={C.lime} x={62} y={32} s={1.6} />,
  tea: <Cup fill={C.tea} x={62} y={32} s={1.6} />,
  pineapple: <Cup fill={C.pineapple} x={62} y={32} s={1.6} />,
  water: (
    <g transform="translate(66 18)">
      <rect x="6" y="18" width="28" height="60" rx="8" fill={C.water} {...stroke} />
      <path d="M13 18 L13 8 L27 8 L27 18 Z" fill={C.water} {...stroke} />
      <rect x="11" y="0" width="18" height="9" rx="2" fill={C.sauce} {...stroke} />
      <rect x="10" y="34" width="20" height="16" rx="2" fill={C.plate} {...stroke} />
    </g>
  ),
  sundae: (
    <g transform="translate(58 16)">
      <path d="M10 30 L34 30 L29 74 L15 74 Z" fill={C.plate} {...stroke} />
      <path d="M8 30 Q22 12 36 30 Z" fill={C.rice} {...stroke} />
      <path d="M12 24 Q22 16 32 24 Q26 28 22 25 Q17 28 12 24 Z" fill={C.choco} {...stroke} />
      <path d="M14 42 L30 42" {...thin} opacity="0.3" fill="none" />
    </g>
  ),
  pie: (
    <g transform="translate(46 34)">
      <path d="M4 12 L54 4 L62 40 L10 48 Z" fill={C.bun} {...stroke} />
      <path d="M18 16 L26 36 M30 13 L38 33 M42 11 L50 31" {...stroke} fill="none" opacity="0.6" />
      <path d="M4 12 L54 4" {...stroke} fill="none" />
    </g>
  ),
  float: (
    <g transform="translate(62 12)">
      <path d="M8 22 L36 22 L31 80 L13 80 Z" fill={C.cola} {...stroke} />
      <circle cx="22" cy="20" r="13" fill={C.rice} {...stroke} />
      <path d="M28 10 L34 -4" {...stroke} fill="none" />
      <circle cx="16" cy="44" r="3" fill={C.rice} opacity="0.5" />
      <circle cx="26" cy="54" r="2.5" fill={C.rice} opacity="0.5" />
    </g>
  ),
  plate: (
    <g>
      <ellipse cx="80" cy="62" rx="44" ry="26" fill={C.plate} {...stroke} />
      <ellipse cx="80" cy="60" rx="30" ry="17" fill={C.card} {...stroke} />
    </g>
  ),
};

/** Name fragments decide the picture; order matters, most specific first. */
const byName: [RegExp, string][] = [
  [/2-?pc.*meal/i, "chicken2Meal"],
  [/chicken.*meal/i, "chickenRiceMeal"],
  [/chicken.*rice/i, "chickenRice"],
  [/chicken sandwich/i, "chickenSandwich"],
  [/burger steak.*meal/i, "burgerSteakMeal"],
  [/burger steak/i, "burgerSteakRice"],
  [/cheeseburger meal/i, "burgerMeal"],
  [/cheeseburger|burger/i, "burger"],
  [/spaghetti meal/i, "spaghettiMeal"],
  [/spaghetti/i, "spaghetti"],
  [/carbonara/i, "carbonara"],
  [/fries/i, "fries"],
  [/coleslaw/i, "coleslaw"],
  [/mashed/i, "mash"],
  [/corn/i, "corn"],
  [/coke float|float/i, "float"],
  [/coke|cola/i, "cola"],
  [/sprite|lemon|lime/i, "lemonLime"],
  [/iced tea|tea/i, "tea"],
  [/pineapple|juice/i, "pineapple"],
  [/water/i, "water"],
  [/sundae/i, "sundae"],
  [/pie/i, "pie"],
];

const byCategory: [RegExp, string][] = [
  [/rice|meal/i, "chickenRice"],
  [/sandwich/i, "burger"],
  [/pasta/i, "spaghetti"],
  [/side/i, "fries"],
  [/drink/i, "cola"],
  [/dessert/i, "sundae"],
];

function pick(name: string, category?: string): string {
  for (const [re, key] of byName) if (re.test(name)) return key;
  if (category) for (const [re, key] of byCategory) if (re.test(category)) return key;
  return "plate";
}

/**
 * A drawing for one product. `name` picks it; `category` is the fallback when the name is unfamiliar,
 * so a newly added product still shows something sensible.
 */
export function FoodArt({ name, category, className = "" }: { name: string; category?: string; className?: string }) {
  return (
    <svg viewBox="0 0 160 120" className={className} role="presentation" aria-hidden="true" focusable="false">
      {scenes[pick(name, category)]}
    </svg>
  );
}
