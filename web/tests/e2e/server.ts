import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { BASE, PORT } from './seed';

// Global setup: build the web app and the server, start the server on its own port with a throwaway data folder,
// and hand Playwright a teardown that stops it (by PID) and deletes the folder.

const web = fileURLToPath(new URL('../../', import.meta.url));
const project = join(web, '..', 'src', 'PKForge.Sync.Server');
// Debug, so a server running from the Release build (the live one) is never touched.
const dll = join(project, 'bin', 'Debug', 'net10.0', 'PKForge.Sync.Server.dll');

const localDotnet = join(homedir(), '.dotnet', 'dotnet');
const dotnet = process.env.DOTNET ?? (existsSync(localDotnet) ? localDotnet : 'dotnet');
const dotnetEnv = { ...process.env, DOTNET_ROOT: process.env.DOTNET_ROOT ?? join(homedir(), '.dotnet'), DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' };

function run(cmd: string, args: string[], cwd: string, env: NodeJS.ProcessEnv = process.env): void {
  const r = spawnSync(cmd, args, { cwd, env, stdio: 'inherit' });
  if (r.status !== 0) throw new Error(`${cmd} ${args.join(' ')} failed (${r.status ?? r.signal})`);
}

async function healthy(): Promise<boolean> {
  try { return (await fetch(`${BASE}/healthz`)).ok; } catch { return false; }
}

async function stop(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return;
  const exited = new Promise<void>((resolve) => child.once('exit', () => resolve()));
  process.kill(child.pid!, 'SIGTERM');
  const timer = setTimeout(() => { try { process.kill(child.pid!, 'SIGKILL'); } catch { /* already gone */ } }, 5_000);
  await exited;
  clearTimeout(timer);
}

export default async function setup(): Promise<() => Promise<void>> {
  if (await healthy()) throw new Error(`Something is already listening on port ${PORT}; stop it first (a stale e2e server?).`);

  if (!process.env.E2E_SKIP_BUILD) {
    run('npm', ['run', 'build'], web);
    run(dotnet, ['build', project, '-c', 'Debug', '-v', 'q', '-nologo'], web, dotnetEnv);
  }

  const data = mkdtempSync(join(tmpdir(), 'pkforge-e2e-'));
  const child = spawn(dotnet, [dll, '--urls', BASE], {
    cwd: join(project, 'bin', 'Debug', 'net10.0'),
    env: {
      ...dotnetEnv,
      ASPNETCORE_ENVIRONMENT: 'Production',
      DATA_DIR: data,
      ALLOW_REGISTRATION: 'true',
      WEB_ROOT: join(web, 'dist'),
      PUBLIC_URL: BASE,
      Sync__AuthRequestsPerMinute: '100000',
      Logging__LogLevel__Default: 'Warning',
    },
    stdio: ['ignore', 'inherit', 'inherit'],
  });

  const teardown = async () => {
    await stop(child);
    rmSync(data, { recursive: true, force: true });
  };

  const deadline = Date.now() + 60_000;
  while (!(await healthy())) {
    if (child.exitCode !== null || Date.now() > deadline) {
      await teardown();
      throw new Error(`The server didn't come up on ${BASE}.`);
    }
    await new Promise((r) => setTimeout(r, 250));
  }
  return teardown;
}
