export interface Selection { selected: Set<number>; anchor: number }

/**
 * One click on a slot.
 * Plain: just that slot, even an empty one. Shift on a Pokémon: adds the occupied range from the anchor.
 * Ctrl/Cmd on a Pokémon: toggles it, never leaving the selection empty. Empty slots are never added by a modifier.
 */
export function select(current: Set<number>, anchor: number, slot: number, occupied: (slot: number) => boolean, mods: { shift: boolean; ctrl: boolean }): Selection {
  const picked = (mods.shift || mods.ctrl) && occupied(slot);
  if (!picked) {
    if (mods.ctrl) return { selected: new Set(current), anchor };
    return { selected: new Set([slot]), anchor: slot };
  }
  const selected = new Set([...current].filter(occupied));
  if (mods.shift) {
    for (let s = Math.min(anchor, slot); s <= Math.max(anchor, slot); s++) if (occupied(s)) selected.add(s);
    return { selected, anchor };
  }
  if (selected.has(slot)) selected.delete(slot); else selected.add(slot);
  if (selected.size === 0) selected.add(slot);
  return { selected, anchor: slot };
}
