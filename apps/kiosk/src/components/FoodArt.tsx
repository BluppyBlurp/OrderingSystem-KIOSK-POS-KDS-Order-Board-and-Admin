/**
 * Built-in food artwork.
 *
 * Every product gets a picture even before anyone uploads a photo in Admin: flat vector drawings in
 * the house palette, matched to the product by name and falling back to the category. They are SVG
 * rather than image files, so they stay sharp on a 1080x1920 kiosk panel, need no hosting, and work
 * with the network down. An uploaded image always wins — see ProductImage.
 *
 * Every drawing shares one stage: a 160x120 viewBox, items standing on the baseline at y=106 and
 * measured from their own bottom centre. That is what keeps a drink and a two-piece meal looking
 * like they belong on the same menu.
 */
import type { ReactNode } from "react";

const INK = "#241612";
const BASELINE = 106;

/** Food colours, chosen to sit beside the chili red and amber without fighting them. */
const C = {
  rice: "#FFFBF2",
  chicken: "#E8A33D",
  chickenDeep: "#C97B22",
  bun: "#E8B562",
  bunDeep: "#CF9540",
  patty: "#653A20",
  gravy: "#8A5A2B",
  gravyLight: "#B07A33",
  lettuce: "#6FA84B",
  sauce: "#C4321F",
  cheese: "#F5B700",
  cream: "#F2E2C4",
  cola: "#4A2318",
  lime: "#8FC63D",
  tea: "#C27A28",
  pineapple: "#F5C518",
  water: "#9ED8E8",
  choco: "#5A3620",
  white: "#FFFFFF",
  card: "#FFF4DE",
} as const;

const ink = { stroke: INK, strokeWidth: 3.2, strokeLinejoin: "round" as const, strokeLinecap: "round" as const };
const hair = { stroke: INK, strokeWidth: 2, strokeLinecap: "round" as const, fill: "none" };

/**
 * Places a drawing on the stage. Each primitive is drawn around its own bottom centre, so `at` is
 * where it stands and `s` scales it in place.
 */
function Stand({ at, s = 1, children }: { at: number; s?: number; children: ReactNode }) {
  return <g transform={`translate(${at} ${BASELINE}) scale(${s})`}>{children}</g>;
}

/* ------------------------------------------------- primitives, drawn from their bottom centre */

/** Drinks cup with lid and straw. w 30, h 62. */
function Cup({ fill }: { fill: string }) {
  return (
    <>
      <path d="M-11 -50 L11 -50 L8 0 L-8 0 Z" fill={fill} {...ink} />
      <rect x="-15" y="-57" width="30" height="9" rx="2.5" fill={C.white} {...ink} />
      <path d="M8 -57 L13 -72" {...ink} fill="none" />
      <path d="M-7 -28 L7 -28" {...hair} opacity="0.3" />
    </>
  );
}

/** Carton of fries. w 36, h 50. */
function Fries() {
  return (
    <>
      <path d="M-13 -36 L-7 -48 L-2 -37 L4 -50 L10 -36 Z" fill={C.cheese} {...ink} />
      <path d="M-16 -36 L16 -36 L12 0 L-12 0 Z" fill={C.sauce} {...ink} />
      <path d="M0 -28 L0 -8" {...hair} opacity="0.45" stroke={C.white} />
    </>
  );
}

/** Mound of rice on a saucer: plate first, mound on top. w 48, h 32. */
function Rice() {
  return (
    <>
      <ellipse cx="0" cy="-4" rx="24" ry="6.5" fill={C.white} {...ink} />
      <path d="M-18 -5 Q-14 -26 0 -31 Q14 -26 18 -5 Z" fill={C.rice} {...ink} />
      <path d="M-8 -12 q4 -5 8 -2 M2 -20 q5 -3 7 1" {...hair} opacity="0.3" />
    </>
  );
}

/**
 * Fried chicken drumstick: meaty bulb up, bone and knuckle down. The bone is what makes it read as
 * chicken rather than a bread roll, so it stays thick enough to survive at thumbnail size.
 */
function Chicken() {
  return (
    <>
      {/* Bone first, then the meat over it: short and thick, or it reads as a lollipop stick. */}
      <path d="M-6 -22 L-15 -8" stroke={C.rice} strokeWidth="10" strokeLinecap="round" fill="none" />
      <path d="M-1 -20 L-11 -5" {...ink} fill="none" />
      <path d="M-11 -25 L-19 -11" {...ink} fill="none" />
      <circle cx="-13" cy="-4" r="5.5" fill={C.rice} {...ink} />
      <circle cx="-19" cy="-8" r="5" fill={C.rice} {...ink} />
      {/* Scalloped edge: the crispy coating is what makes this read as fried chicken at thumbnail size. */}
      <path
        d="M-7 -23 q-8 -2 -7 -9 q-6 -5 -1 -11 q-2 -8 6 -9 q2 -7 10 -5 q6 -5 11 1 q8 1 7 9 q6 5 1 11 q2 8 -7 8 q-3 6 -10 4 q-7 4 -10 0 Z"
        fill={C.chicken}
        {...ink}
      />
      <path d="M0 -40 q6 3 3 10 M9 -33 q5 2 3 8" {...hair} opacity="0.4" />
    </>
  );
}

/** Stacked burger. w 54, h 54. */
function Burger({ cheese = true }: { cheese?: boolean }) {
  return (
    <>
      <path d="M-25 -34 Q-25 -54 0 -54 Q25 -54 25 -34 Z" fill={C.bun} {...ink} />
      <circle cx="-11" cy="-44" r="1.7" fill={INK} opacity="0.35" />
      <circle cx="2" cy="-47" r="1.7" fill={INK} opacity="0.35" />
      <circle cx="12" cy="-42" r="1.7" fill={INK} opacity="0.35" />
      <path d="M-27 -34 L27 -34 L27 -28 L-27 -28 Z" fill={C.lettuce} {...ink} />
      {cheese && <path d="M-25 -28 L25 -28 L19 -20 L-19 -20 Z" fill={C.cheese} {...ink} />}
      <rect x="-26" y={cheese ? -22 : -28} width="52" height="11" rx="4.5" fill={C.patty} {...ink} />
      <path d="M-25 -11 Q-25 0 0 0 Q25 0 25 -11 Z" fill={C.bunDeep} {...ink} />
    </>
  );
}

/** Bowl of noodles. Sauce colour tells spaghetti from carbonara. w 52, h 42. */
function Noodles({ sauce }: { sauce: string }) {
  return (
    <>
      <path d="M-24 -22 Q0 -40 24 -22 Z" fill={C.rice} {...ink} />
      <path d="M-16 -26 Q0 -34 16 -26 Q8 -20 0 -24 Q-8 -20 -16 -26 Z" fill={sauce} {...ink} />
      <path d="M-26 -22 Q-24 0 0 0 Q24 0 26 -22 Z" fill={C.white} {...ink} />
      <path d="M-14 -12 Q0 -6 14 -12" {...hair} opacity="0.25" />
    </>
  );
}

/** Tub with a domed filling — coleslaw, mash, corn. w 40, h 42. */
function Tub({ fill, dots }: { fill: string; dots?: string }) {
  return (
    <>
      <path d="M-18 -28 Q0 -42 18 -28 Z" fill={fill} {...ink} />
      <path d="M-20 -28 L20 -28 L16 0 L-16 0 Z" fill={C.card} {...ink} />
      {dots && (
        <g fill={dots}>
          <circle cx="-7" cy="-31" r="2.4" />
          <circle cx="1" cy="-34" r="2.4" />
          <circle cx="8" cy="-30" r="2.4" />
        </g>
      )}
    </>
  );
}

/** Burger steak: patty sitting in a pool of gravy. w 52, h 26. */
function Steak() {
  return (
    <>
      <ellipse cx="0" cy="-6" rx="26" ry="11" fill={C.gravyLight} {...ink} />
      <rect x="-19" y="-23" width="38" height="15" rx="7" fill={C.patty} {...ink} />
      <path d="M-11 -18 q11 -4 22 0" {...hair} stroke={C.gravyLight} opacity="0.55" />
    </>
  );
}

/* ------------------------------------------------------------------------------------ scenes */

/** A meal: the main dish centre stage, fries and a drink flanking it. */
function Meal({ children }: { children: ReactNode }) {
  return (
    <>
      <Stand at={28} s={0.8}>
        <Fries />
      </Stand>
      <Stand at={132} s={0.8}>
        <Cup fill={C.cola} />
      </Stand>
      {children}
    </>
  );
}

const scenes: Record<string, ReactNode> = {
  chickenRice: (
    <>
      <Stand at={96} s={1.25}>
        <Rice />
      </Stand>
      <Stand at={62} s={1.25}>
        <Chicken />
      </Stand>
    </>
  ),
  chickenRiceMeal: (
    <Meal>
      <Stand at={88} s={0.95}>
        <Rice />
      </Stand>
      <Stand at={66} s={0.95}>
        <Chicken />
      </Stand>
    </Meal>
  ),
  chicken2Meal: (
    <Meal>
      <Stand at={92} s={0.9}>
        <Rice />
      </Stand>
      <Stand at={60} s={0.85}>
        <Chicken />
      </Stand>
      <Stand at={80} s={0.85}>
        <Chicken />
      </Stand>
    </Meal>
  ),
  burgerSteakRice: (
    <>
      <Stand at={102} s={1.2}>
        <Rice />
      </Stand>
      <Stand at={58} s={1.2}>
        <Steak />
      </Stand>
    </>
  ),
  burgerSteakMeal: (
    <Meal>
      <Stand at={94} s={0.95}>
        <Rice />
      </Stand>
      <Stand at={68} s={0.95}>
        <Steak />
      </Stand>
    </Meal>
  ),
  burger: (
    <Stand at={80} s={1.35}>
      <Burger />
    </Stand>
  ),
  burgerMeal: (
    <Meal>
      <Stand at={80} s={1}>
        <Burger />
      </Stand>
    </Meal>
  ),
  chickenSandwich: (
    <Stand at={80} s={1.35}>
      <Burger cheese={false} />
    </Stand>
  ),
  spaghetti: (
    <Stand at={80} s={1.55}>
      <Noodles sauce={C.sauce} />
    </Stand>
  ),
  spaghettiMeal: (
    <Meal>
      <Stand at={80} s={1.1}>
        <Noodles sauce={C.sauce} />
      </Stand>
    </Meal>
  ),
  carbonara: (
    <Stand at={80} s={1.55}>
      <Noodles sauce={C.cream} />
    </Stand>
  ),
  fries: (
    <Stand at={80} s={1.7}>
      <Fries />
    </Stand>
  ),
  coleslaw: (
    <Stand at={80} s={1.7}>
      <Tub fill={C.lettuce} dots={C.rice} />
    </Stand>
  ),
  mash: (
    <Stand at={80} s={1.7}>
      <Tub fill={C.rice} dots={C.gravy} />
    </Stand>
  ),
  corn: (
    <Stand at={80} s={1.7}>
      <Tub fill={C.cheese} dots={C.chickenDeep} />
    </Stand>
  ),
  cola: (
    <Stand at={80} s={1.5}>
      <Cup fill={C.cola} />
    </Stand>
  ),
  lemonLime: (
    <Stand at={80} s={1.5}>
      <Cup fill={C.lime} />
    </Stand>
  ),
  tea: (
    <Stand at={80} s={1.5}>
      <Cup fill={C.tea} />
    </Stand>
  ),
  pineapple: (
    <Stand at={80} s={1.5}>
      <Cup fill={C.pineapple} />
    </Stand>
  ),
  water: (
    <Stand at={80} s={1.4}>
      <rect x="-14" y="-44" width="28" height="44" rx="7" fill={C.water} {...ink} />
      <path d="M-7 -44 L-7 -54 L7 -54 L7 -44 Z" fill={C.water} {...ink} />
      <rect x="-9" y="-62" width="18" height="9" rx="2" fill={C.sauce} {...ink} />
      <rect x="-10" y="-32" width="20" height="15" rx="2" fill={C.white} {...ink} />
    </Stand>
  ),
  sundae: (
    <Stand at={80} s={1.35}>
      <path d="M-12 -34 L12 -34 L8 0 L-8 0 Z" fill={C.white} {...ink} />
      <path d="M-14 -34 Q0 -52 14 -34 Z" fill={C.rice} {...ink} />
      <path d="M-10 -40 Q0 -48 10 -40 Q4 -44 0 -41 Q-5 -44 -10 -40 Z" fill={C.choco} {...ink} />
      <path d="M-6 -22 L6 -22" {...hair} opacity="0.25" />
    </Stand>
  ),
  pie: (
    <Stand at={80} s={1.35}>
      <path d="M-28 -6 L22 -14 L30 -44 L-22 -36 Z" fill={C.bun} {...ink} />
      <path d="M-12 -34 L-6 -12 M0 -36 L6 -14 M12 -38 L18 -17" {...ink} fill="none" opacity="0.55" />
      <path d="M-22 -36 L30 -44" {...ink} fill="none" />
    </Stand>
  ),
  float: (
    <Stand at={80} s={1.25}>
      <path d="M-14 -52 L14 -52 L10 0 L-10 0 Z" fill={C.cola} {...ink} />
      <circle cx="0" cy="-54" r="14" fill={C.rice} {...ink} />
      <path d="M8 -64 L14 -80" {...ink} fill="none" />
      <circle cx="-5" cy="-30" r="3" fill={C.rice} opacity="0.45" />
      <circle cx="5" cy="-20" r="2.5" fill={C.rice} opacity="0.45" />
    </Stand>
  ),
  plate: (
    <Stand at={80} s={1.3}>
      <ellipse cx="0" cy="-10" rx="34" ry="16" fill={C.white} {...ink} />
      <ellipse cx="0" cy="-12" rx="22" ry="10" fill={C.card} {...ink} />
    </Stand>
  ),
};

/**
 * A tight box per scene, computed from the primitives' extents, so a lone drink and a full meal
 * each fill their frame instead of floating in shared empty space.
 */
const boxes: Record<string, string> = {
  chickenRice: "33.5 35 98.5 80.8",
  chickenRiceMeal: "9.2 42.4 140.8 72.4",
  chicken2Meal: "9.2 42.4 140.8 72.2",
  burgerSteakRice: "20.8 59.2 116 58.8",
  burgerSteakMeal: "9.2 42.4 140.8 74.4",
  burger: "37.6 27.1 84.9 84.9",
  burgerMeal: "9.2 42.4 140.8 69.6",
  chickenSandwich: "37.6 27.1 84.9 84.9",
  spaghetti: "33.7 38 92.6 74",
  spaghettiMeal: "9.2 42.4 140.8 69.6",
  carbonara: "33.7 38 92.6 74",
  fries: "46.8 15 66.4 97",
  coleslaw: "40 28.6 80 83.4",
  mash: "40 28.6 80 83.4",
  corn: "40 28.6 80 83.4",
  cola: "51.5 -8 57 120",
  lemonLime: "51.5 -8 57 120",
  tea: "51.5 -8 57 120",
  pineapple: "51.5 -8 57 120",
  water: "54.4 13.2 51.2 98.8",
  sundae: "55.1 29.8 49.8 82.2",
  pie: "36.2 40.6 90.3 63.3",
  float: "56.5 0 47 112",
  plate: "29.8 66.2 100.4 53.6",
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
  const key = pick(name, category);
  return (
    <svg
      viewBox={boxes[key] ?? "0 0 160 120"}
      className={className}
      role="presentation"
      aria-hidden="true"
      focusable="false"
    >
      {scenes[key]}
    </svg>
  );
}
