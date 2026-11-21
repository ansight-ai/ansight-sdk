"use strict";
const { app } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict");
const { installDatabase } = require("../lib/database");
const root = process.env.ANSIGHT_DATABASE_FIXTURE_DIR,
  modulePath = process.env.ANSIGHT_SQLITE_MODULE;
if (!root || !modulePath)
  throw new Error("Fixture directory and SQLite module required");
fs.mkdirSync(root, { recursive: true });
app.setPath("userData", path.join(root, "user-data"));
const timer = setTimeout(() => app.exit(2), 20000);
app
  .whenReady()
  .then(async () => {
    const Database = require(modulePath),
      file = path.join(root, "sample.sqlite"),
      db = new Database(file);
    db.exec(
      "DROP TABLE IF EXISTS sample; CREATE TABLE sample(id INTEGER PRIMARY KEY, title TEXT, payload BLOB); INSERT INTO sample VALUES (1,'Résumé',x'0001ff'),(2,'second',NULL)",
    );
    db.close();
    const tools = new Map();
    installDatabase({
      options: { databases: { fixture: file }, sqliteModulePath: modulePath },
      registerTool: (d, h) => tools.set(d.id, { d, h }),
    });
    const call = (name, a = {}) => tools.get(`data.${name}`).h(a);
    const checks = [];
    assert.equal((await call("list_databases")).databases[0].path, file);
    checks.push("registered database discovery");
    assert.equal(
      (await call("describe_schema", { path: "fixture" })).tables[0].columns
        .length,
      3,
    );
    checks.push("actual schema metadata");
    const result = await call("query", {
      path: "fixture",
      sql: "SELECT id AS x,id AS x,title,payload FROM sample ORDER BY id",
      maxRows: 1,
    });
    assert.equal(result.truncated, true);
    assert.deepEqual(result.columns, ["x", "x", "title", "payload"]);
    assert.equal(result.rows[0].x_2, 1);
    assert.equal(result.rows[0].payload.base64, "AAH/");
    checks.push("duplicate columns, bounded rows, UTF8 and blob metadata");
    for (const sql of [
      "DELETE FROM sample",
      "INSERT INTO sample(title) VALUES ('bad') RETURNING id",
      "PRAGMA query_only=OFF",
      "SELECT 1; DELETE FROM sample",
      "ATTACH DATABASE ':memory:' AS other",
    ]) {
      await assert.rejects(call("query", { path: "fixture", sql }));
    }
    checks.push("write, returning write, pragma mutation and attach rejected");
    await assert.rejects(
      call("query", { path: "/etc/passwd", sql: "SELECT 1" }),
      /not registered/,
    );
    checks.push("unregistered path rejected");
    await assert.rejects(
      call("query", { path: "fixture", sql: "SELECT zeroblob(1048577)" }),
      /exceeds 1 MiB/,
    );
    checks.push("oversize blob rejected");
    const link = path.join(root, "link.sqlite");
    try {
      fs.unlinkSync(link);
    } catch {}
    fs.symlinkSync(file, link);
    const links = new Map();
    installDatabase({
      options: { databases: { linked: link }, sqliteModulePath: modulePath },
      registerTool: (d, h) => links.set(d.id, h),
    });
    await assert.rejects(
      links.get("data.query")({ path: "linked", sql: "SELECT 1" }),
      /symlink/,
    );
    checks.push("symlink database rejected");
    const big = await call("query", {
      path: "fixture",
      sql: "SELECT 9223372036854775807 AS n",
    });
    assert.equal(big.rows[0].n, "9223372036854775807");
    checks.push("64-bit integer preserved without rounding");
    const large = await call("query", {
      path: "fixture",
      sql: "SELECT hex(zeroblob(100000)) AS data",
    });
    assert.equal(large.rows[0].data.length, 200000);
    checks.push(
      "large IPC response delivered completely before subprocess exit",
    );
    const start = Date.now();
    await assert.rejects(
      call("query", {
        path: "fixture",
        sql: "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n) SELECT sum(x) FROM n",
      }),
      /time limit/,
    );
    assert.ok(Date.now() - start < 6000);
    checks.push("unbounded query killed without blocking app");
    const after = await call("query", {
      path: "fixture",
      sql: "SELECT count(*) AS count FROM sample",
    });
    assert.equal(after.rows[0].count, 2);
    checks.push("data unchanged and query succeeds after timeout");
    fs.writeFileSync(
      path.join(root, "report.json"),
      JSON.stringify({ passed: true, checks, query: result }, null, 2),
    );
    console.log(JSON.stringify({ passed: true, checks }));
    clearTimeout(timer);
    app.quit();
  })
  .catch((error) => {
    console.error(error);
    app.exit(1);
  });
