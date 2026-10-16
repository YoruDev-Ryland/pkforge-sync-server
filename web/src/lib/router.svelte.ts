export type Route = { page: 'signin' } | { page: 'account' } | { page: 'released' } | { page: 'bank'; box: number | null };

/** Recently released, reached from the Bank's sidebar (or the box picker on phones). */
export const RELEASED_PATH = '/bank/released';

/** `/bank/{n}` is one-based in the address bar and zero-based in the route. */
export function parseRoute(path: string): Route {
  if (path === '/signin') return { page: 'signin' };
  if (path === '/account') return { page: 'account' };
  if (path === RELEASED_PATH) return { page: 'released' };
  const m = /^\/bank\/(\d+)$/.exec(path);
  const n = m ? Number(m[1]) : 0;
  return { page: 'bank', box: n >= 1 ? n - 1 : null };
}

/** The box on screen: the route's box on Bank pages (past the last one, the last), else the last box viewed. */
export function shownBox(route: Route, lastViewed: number, count: number): number {
  if (route.page === 'bank' && route.box !== null) return Math.max(0, Math.min(route.box, count - 1));
  return lastViewed;
}

class Router {
  path = $state(location.pathname);
  route: Route = $derived(parseRoute(this.path));
  constructor() { addEventListener('popstate', () => (this.path = location.pathname)); }
  go(path: string, replace = false) {
    if (path === this.path) return;
    history[replace ? 'replaceState' : 'pushState'](null, '', path);
    this.path = path;
  }
}
export const router = new Router();
