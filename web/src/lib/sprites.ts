import type { MonInfo } from './protocol';

// Port of the app's SpriteCatalog.PkhexName and the pixel-sprite part of BundledCandidates
// (PKForge/src/PKForge.Domain/SpriteCatalog.cs). Checked against fixtures/sprite-cases.json.

const PIKACHU = 25, EEVEE = 133, ALCREMIE = 869;
const DEFAULT_FORM_SPRITE = new Set([414, 664, 665, 744, 778, 854, 855, 892, 982, 1012, 1013]);
const GENDERED_SPRITE = new Set([449, 450, 521, 592, 593, 668]);

function totemBaseForm(species: number, form: number): number | null {
  if (species === 778 && (form === 2 || form === 3)) return 0;
  if ((species === 20 || species === 105) && form === 2) return 1;
  if ([735, 738, 743, 752, 754, 758, 777, 784].includes(species) && form === 1) return 0;
  return null;
}

export function pkhexName(species: number, form: number, female: boolean, formArgument: number, cosplay: boolean, shiny: boolean, fold = true): string {
  if (fold && DEFAULT_FORM_SPRITE.has(species)) form = 0;
  let s = `_${species}`;
  if (form !== 0) {
    s += `_${form}`;
    if (species === PIKACHU) { if (cosplay) s += 'c'; else if (form === 8) s += 'p'; }
    else if (species === EEVEE && form === 1) s += 'p';
  }
  if (female && GENDERED_SPRITE.has(species)) s += 'f';
  if (species === ALCREMIE) { if (form === 0) s += `_${form}`; s += `_${formArgument}`; }
  if (shiny) s += 's';
  return s;
}

const stem = (resource: string) => resource.slice(1).replaceAll('_', '-');
const url = (file: string) => `/pkhex/sprites/b_${file}.png`;

/** Sprite URLs to try for a Pokemon, best first; always ends with the placeholder b_0. */
export function spriteCandidates(info: MonInfo): string[] {
  const t = { female: !!info.traits?.female, formArgument: info.traits?.formArgument ?? 0, cosplay: !!info.traits?.cosplay, gigantamax: !!info.traits?.gigantamax };
  const species = info.species, shiny = info.shiny;
  const form = totemBaseForm(species, info.form) ?? info.form;
  const list: string[] = [];
  const add = (u: string) => { if (!list.includes(u)) list.push(u); };
  const B = (female: boolean, s: boolean, fold = true) => url(stem(pkhexName(species, form, female, t.formArgument, t.cosplay, s, fold)));

  if (t.gigantamax) {
    const g = species === 892 && form !== 0 ? `${species}-${form}-gmax` : `${species}-gmax`;
    if (shiny) add(url(`${g}s`));
    add(url(g));
  }
  const gendered = t.female && GENDERED_SPRITE.has(species);
  for (const fold of form !== 0 && DEFAULT_FORM_SPRITE.has(species) ? [false, true] : [true]) {
    if (shiny) add(B(t.female, true, fold));
    add(B(t.female, false, fold));
    if (gendered) { if (shiny) add(B(false, true, fold)); add(B(false, false, fold)); }
  }
  if (form !== 0 || t.cosplay || species === ALCREMIE) {
    const base = species === ALCREMIE ? `${species}-0-0` : `${species}`;
    if (shiny) add(url(`${base}s`));
    add(url(base));
  }
  add(url('0'));
  return list;
}

export const ballUrl = (ball: number) => `/pkhex/balls/_ball${ball}.png`;
