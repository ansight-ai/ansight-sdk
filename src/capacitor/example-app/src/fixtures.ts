import {
  Directory,
  Encoding,
  Filesystem,
  type StatResult,
} from "@capacitor/filesystem";
import { Preferences } from "@capacitor/preferences";
import initSqlJs from "sql.js";
import sqlWasmUrl from "sql.js/dist/sql-wasm.wasm?url";

export interface HarnessFixtureSummary {
  seededAtUtc: string;
  dataDirectory: string;
  cacheDirectory: string;
  databasePath: string;
  databaseBytes: number;
  databaseOrderCount: number;
  databaseEventCount: number;
  latestOrder: string | null;
  dataFile: StatResult;
  cacheFile: StatResult;
  preferenceKeys: string[];
  launchCount: number;
}

export interface HarnessDatabaseSummary {
  databaseBytes: number;
  databaseOrderCount: number;
  databaseEventCount: number;
  latestOrder: string | null;
}

const fixtureDirectory = "ansight-capacitor-harness";
const dataFilePath = `${fixtureDirectory}/harness-document.json`;
const cacheFilePath = `${fixtureDirectory}/harness-cache.txt`;
const databasePath = `${fixtureDirectory}/ansight_capacitor_harness.sqlite`;
const preferencePrefix = "ansight.harness.";

function bytesToBase64(bytes: Uint8Array): string {
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  }
  return btoa(binary);
}

function base64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

async function ensureDirectory(
  path: string,
  directory: Directory,
): Promise<void> {
  try {
    await Filesystem.mkdir({ path, directory, recursive: true });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (!/exist/i.test(message)) throw error;
  }
}

async function createDatabase(seededAtUtc: string): Promise<Uint8Array> {
  const SQL = await initSqlJs({ locateFile: () => sqlWasmUrl });
  const database = new SQL.Database();
  database.run(`
    CREATE TABLE harness_orders (
      id INTEGER PRIMARY KEY,
      customer TEXT NOT NULL,
      total_cents INTEGER NOT NULL,
      status TEXT NOT NULL
    );
    INSERT INTO harness_orders (customer, total_cents, status) VALUES
      ('Ada', 1299, 'ready'),
      ('Grace', 4200, 'processing'),
      ('Linus', 875, 'complete');

    CREATE TABLE harness_events (
      id INTEGER PRIMARY KEY,
      kind TEXT NOT NULL,
      created_at_utc TEXT NOT NULL,
      payload_json TEXT NOT NULL
    );
    INSERT INTO harness_events (kind, created_at_utc, payload_json)
    VALUES ('fixture.seeded', '${seededAtUtc}', '{"framework":"capacitor","count":3}');
  `);
  const bytes = database.export();
  database.close();
  return bytes;
}

export async function seedHarnessFixtures(): Promise<HarnessFixtureSummary> {
  const seededAtUtc = new Date().toISOString();
  await Promise.all([
    ensureDirectory(fixtureDirectory, Directory.Data),
    ensureDirectory(fixtureDirectory, Directory.Cache),
  ]);

  const previousLaunchCount = Number(
    (await Preferences.get({ key: `${preferencePrefix}launchCount` })).value ??
      "0",
  );
  const launchCount = Number.isFinite(previousLaunchCount)
    ? previousLaunchCount + 1
    : 1;

  const databaseBytes = await createDatabase(seededAtUtc);
  await Promise.all([
    Filesystem.writeFile({
      path: dataFilePath,
      directory: Directory.Data,
      encoding: Encoding.UTF8,
      recursive: true,
      data: JSON.stringify(
        {
          schema: "ai.ansight.capacitor-harness.fixture.v1",
          framework: "capacitor",
          seededAtUtc,
          features: ["files", "preferences", "sqlite", "secure-storage"],
        },
        null,
        2,
      ),
    }),
    Filesystem.writeFile({
      path: cacheFilePath,
      directory: Directory.Cache,
      encoding: Encoding.UTF8,
      recursive: true,
      data: `Ansight Capacitor cache fixture\nseededAtUtc=${seededAtUtc}\n`,
    }),
    Filesystem.writeFile({
      path: databasePath,
      directory: Directory.Data,
      recursive: true,
      data: bytesToBase64(databaseBytes),
    }),
    Preferences.set({ key: `${preferencePrefix}mode`, value: "validation" }),
    Preferences.set({
      key: `${preferencePrefix}seededAtUtc`,
      value: seededAtUtc,
    }),
    Preferences.set({
      key: `${preferencePrefix}launchCount`,
      value: String(launchCount),
    }),
  ]);

  const [dataFile, cacheFile, dataUri, cacheUri, preferenceKeys] =
    await Promise.all([
      Filesystem.stat({ path: dataFilePath, directory: Directory.Data }),
      Filesystem.stat({ path: cacheFilePath, directory: Directory.Cache }),
      Filesystem.getUri({ path: fixtureDirectory, directory: Directory.Data }),
      Filesystem.getUri({ path: fixtureDirectory, directory: Directory.Cache }),
      Preferences.keys(),
    ]);

  if (
    databaseBytes.length === 0 ||
    dataFile.size === 0 ||
    cacheFile.size === 0
  ) {
    throw new Error(
      "One or more native fixtures were created without content.",
    );
  }

  return {
    seededAtUtc,
    dataDirectory: dataUri.uri,
    cacheDirectory: cacheUri.uri,
    databasePath,
    databaseBytes: databaseBytes.length,
    databaseOrderCount: 3,
    databaseEventCount: 1,
    latestOrder: "Linus",
    dataFile,
    cacheFile,
    preferenceKeys: preferenceKeys.keys.filter((key) =>
      key.startsWith(preferencePrefix),
    ),
    launchCount,
  };
}

export async function insertGeneratedOrder(): Promise<{
  label: string;
  summary: HarnessDatabaseSummary;
}> {
  const databaseFile = await Filesystem.readFile({
    path: databasePath,
    directory: Directory.Data,
  });
  if (typeof databaseFile.data !== "string") {
    throw new Error("The native SQLite fixture was not returned as base64.");
  }

  const SQL = await initSqlJs({ locateFile: () => sqlWasmUrl });
  const database = new SQL.Database(base64ToBytes(databaseFile.data));
  const label = `Generated ${Date.now() % 100000}`;
  const createdAtUtc = new Date().toISOString();
  database.run(
    "INSERT INTO harness_orders (customer, total_cents, status) VALUES (?, ?, ?)",
    [label, 4250, "generated"],
  );
  database.run(
    "INSERT INTO harness_events (kind, created_at_utc, payload_json) VALUES (?, ?, ?)",
    ["database.insert", createdAtUtc, JSON.stringify({ label })],
  );
  const bytes = database.export();
  const summary = readSummary(database, bytes.length);
  database.close();
  await Filesystem.writeFile({
    path: databasePath,
    directory: Directory.Data,
    recursive: true,
    data: bytesToBase64(bytes),
  });
  return { label, summary };
}

function readSummary(
  database: import("sql.js").Database,
  databaseBytes: number,
): HarnessDatabaseSummary {
  const scalar = (sql: string): string | number | null => {
    const result = database.exec(sql)[0];
    const value = result?.values[0]?.[0] ?? null;
    return typeof value === "string" || typeof value === "number"
      ? value
      : null;
  };
  return {
    databaseBytes,
    databaseOrderCount: Number(
      scalar("SELECT COUNT(*) FROM harness_orders") ?? 0,
    ),
    databaseEventCount: Number(
      scalar("SELECT COUNT(*) FROM harness_events") ?? 0,
    ),
    latestOrder:
      String(
        scalar(
          "SELECT customer FROM harness_orders ORDER BY id DESC LIMIT 1",
        ) ?? "",
      ) || null,
  };
}
