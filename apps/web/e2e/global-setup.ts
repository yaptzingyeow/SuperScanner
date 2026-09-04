import { execFileSync, spawn, type ChildProcess } from 'node:child_process';
import { resolve } from 'node:path';

const webRoot = resolve(__dirname, '..');
const repositoryRoot = resolve(__dirname, '../../..');
const localConnection =
  'Host=127.0.0.1;Port=5433;Database=superscanner;Username=superscanner_local;Password=superscanner_local_only';
const sharedEnvironment = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: 'E2E',
  ConnectionStrings__PostgreSql: localConnection,
  R2__ServiceUrl: 'http://127.0.0.1:9000',
  R2__AccessKeyId: 'superscanner_local',
  R2__SecretAccessKey: 'superscanner_local_only_password',
  R2__BucketName: 'superscanner-private',
  Audit__SigningKeyBase64: 'MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=',
  Audit__SigningKeyId: 'e2e-local-only',
};

function start(command: string, args: string[], environment = process.env): ChildProcess {
  return spawn(command, args, {
    cwd: webRoot,
    detached: process.platform !== 'win32',
    env: environment,
    stdio: 'ignore',
    windowsHide: true,
  });
}

async function waitUntilHealthy(url: string, processToWatch: ChildProcess): Promise<void> {
  const deadline = Date.now() + 120_000;
  while (Date.now() < deadline) {
    if (processToWatch.exitCode !== null) {
      throw new Error(`E2E service exited before becoming healthy: ${url}`);
    }

    try {
      const response = await fetch(url);
      if (response.ok) return;
    } catch {
      // The service is still starting.
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`Timed out waiting for E2E service: ${url}`);
}

function stop(processToStop: ChildProcess): void {
  if (!processToStop.pid || processToStop.exitCode !== null) return;

  try {
    if (process.platform === 'win32') {
      execFileSync('taskkill', ['/pid', String(processToStop.pid), '/t', '/f'], { stdio: 'ignore' });
    } else {
      process.kill(-processToStop.pid, 'SIGTERM');
    }
  } catch {
    // A process that already exited needs no further cleanup.
  }
}

export default async function globalSetup(): Promise<() => Promise<void>> {
  const api = start(
    'dotnet',
    [resolve(repositoryRoot, 'src/SuperScanner.Api/bin/Debug/net10.0/SuperScanner.Api.dll')],
    {
      ...sharedEnvironment,
      ASPNETCORE_URLS: 'http://127.0.0.1:5080',
      E2E__IdentityFixtureEnabled: 'true',
    },
  );
  const worker = start(
    'dotnet',
    [resolve(repositoryRoot, 'src/SuperScanner.Worker/bin/Debug/net10.0/SuperScanner.Worker.dll')],
    {
      ...sharedEnvironment,
      ASPNETCORE_URLS: 'http://127.0.0.1:5081',
      ClamAv__Host: '127.0.0.1',
      ClamAv__Port: '3310',
    },
  );
  const web = start(process.execPath, ['e2e/server.mjs']);
  const processes = [api, worker, web];

  try {
    await Promise.all([
      waitUntilHealthy('http://127.0.0.1:5080/health', api),
      waitUntilHealthy('http://127.0.0.1:5081/health', worker),
      waitUntilHealthy('http://127.0.0.1:4300', web),
    ]);
  } catch (error) {
    processes.reverse().forEach(stop);
    throw error;
  }

  return async () => {
    processes.reverse().forEach(stop);
  };
}
