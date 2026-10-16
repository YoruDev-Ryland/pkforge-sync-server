// Mirrors PKForge.Sync.Protocol (Messages.cs) and PKForge.Sync.Server.Web.Details.MonDetails; JSON is camelCase, enums camelCase strings.

export type ItemKind = 'mon' | 'boxes' | 'settings';

export interface MonInfo {
  species: number;
  form: number;
  shiny: boolean;
  nickname: string;
  level: number;
  generation: number;
  sourceName: string;
  format?: string;
  heldItem?: number;
  traits?: { female?: boolean; formArgument?: number; cosplay?: boolean; gigantamax?: boolean };
}

export interface MonValue {
  box: number;
  slot: number;
  movedUtc: string;
  info: MonInfo;
  addedUtc: string;
  blob: string;
}

export interface BoxesValue {
  count: number;
  names?: (string | null)[];
  wallpapers?: (string | null)[];
}

export interface SettingsValue {
  livingDexStartBox?: number | null;
  [k: string]: unknown;
}

export interface SyncItem {
  kind: ItemKind | string;
  id: string;
  version: number;
  deleted: boolean;
  value: unknown | null;
}

export interface ItemWrite {
  kind: ItemKind | string;
  id: string;
  baseVersion: number;
  value: unknown | null;
  deleted?: boolean;
}

export interface ChangeGroup {
  clientGroupId: string;
  writes: ItemWrite[];
}

export interface PushRequest {
  groups: ChangeGroup[];
  since?: number | null;
  epoch?: string | null;
}

export type GroupStatus = 'accepted' | 'conflict' | 'slotTaken' | 'rejected';

export interface GroupResult {
  clientGroupId: string;
  status: GroupStatus;
  items: SyncItem[];
  error?: string;
  message?: string;
}

export interface PushResponse {
  results: GroupResult[];
  version: number;
  epoch: string;
}

export interface ChangesResponse {
  items: SyncItem[];
  next: number;
  hasMore: boolean;
  epoch: string;
}

/** One released Pokémon the web Bank can still restore: push `value` on top of `version` (its tombstone). */
export interface ReleasedMon {
  id: string;
  version: number;
  releasedUtc: string;
  value: MonValue;
}

/** GET /v1/bank/released. `retentionDays` 0: releases on this server are final. */
export interface ReleasedResponse {
  retentionDays: number;
  items: ReleasedMon[];
}

export interface ErrorResponse {
  code: string;
  message: string;
}

export interface ServerInfo {
  minProtocol: number;
  maxProtocol: number;
  /** The operator's name for this server. */
  name: string;
  registrationOpen: boolean;
  emailEnabled: boolean;
  emailVerificationRequired: boolean;
}

export interface DeviceInfo {
  id: string;
  name: string;
  createdUtc: string;
  lastSyncUtc: string | null;
  isCurrent: boolean;
}

export interface PairingCodeResponse {
  code: string;
  qrPayload: string;
  expiresUtc: string;
}

export interface WebSessionRequest {
  email?: string | null;
  password?: string | null;
  pairingCode?: string | null;
  deviceName: string;
}

export interface WebSessionResponse {
  accountId: string;
  deviceId: string;
  emailVerified: boolean;
}

export interface TrainerDetails { name: string; gender: string; tid: string; sid: string | null }
export interface MetDetails { location: string | null; level: number | null; date: string | null; hatched: boolean; eggLocation: string | null; eggDate: string | null }
export interface HiddenPowerDetails { type: string; power: number }
export interface MoveDetails { name: string; type: string; category: string; power: number; accuracy: number; pp: number; maxPp: number; ppUps: number }
export interface ContestDetails { cool: number; beauty: number; cute: number; smart: number; tough: number; sheen: number }
export interface ExtraDetails { teraType?: string | null; dynamaxLevel?: number | null; scale?: number | null; alpha?: boolean | null }

/** Everything the inspector shows about one Pokémon. Stat lists are HP, Atk, Def, SpA, SpD, Spe. */
export interface MonDetails {
  available: boolean;
  format: string | null;
  generation: number;
  size: number;
  checksumValid: boolean;
  pid: string | null;
  encryptionConstant: string | null;
  species: number;
  speciesName: string | null;
  form: number;
  formName: string | null;
  nickname: string | null;
  isNicknamed: boolean;
  isEgg: boolean;
  gender: 'male' | 'female' | 'genderless' | string;
  level: number;
  exp: number;
  expThisLevel: number;
  expNextLevel: number | null;
  growthRate: string | null;
  types: string[];
  nature: string | null;
  natureUp: number | null;
  natureDown: number | null;
  statNature: string | null;
  ability: string | null;
  heldItem: string | null;
  ball: string | null;
  ballId: number;
  shiny: boolean;
  markings: boolean[];
  pokerus: string | null;
  friendship: number;
  ot: TrainerDetails | null;
  met: MetDetails | null;
  game: string | null;
  language: string | null;
  baseStats: number[];
  ivs: number[];
  evs: number[];
  stats: number[];
  ivMax: number;
  hiddenPower: HiddenPowerDetails | null;
  moves: MoveDetails[];
  contest: ContestDetails | null;
  ribbons: string[];
  legal: boolean | null;
  legalityNotes: string[];
  extra: ExtraDetails;
}

export const SLOTS_PER_BOX = 30;
export const key = (kind: string, id: string) => `${kind}:${id}`;
export const BOXES_KEY = 'boxes:bank';
export const SETTINGS_KEY = 'settings:bank';
