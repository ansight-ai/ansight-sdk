"use strict";
function query(db, sql, maxRows) {
  const statement = db.prepare(sql);
  if (!statement.readonly || !statement.reader)
    throw new Error("Only read-only SQLite result statements are supported");
  statement.raw(true).safeIntegers(true);
  const used = new Set();
  const columnMetadata = statement.columns().map((c, index) => {
    const base = c.name.trim() ? c.name : `column_${index}`;
    let key = base,
      suffix = 2;
    while (used.has(key)) key = `${base}_${suffix++}`;
    used.add(key);
    return {
      index,
      name: c.name,
      key,
      declaredType: c.type,
      sourceDatabase: c.database,
      sourceTable: c.table,
      sourceColumn: c.column,
    };
  });
  const rows = [],
    rowValues = [];
  let truncated = false,
    bytes = 0;
  for (const row of statement.iterate()) {
    if (rows.length >= maxRows) {
      truncated = true;
      break;
    }
    const cells = row.map((value, index) => {
      let storageType = "null";
      if (Buffer.isBuffer(value)) {
        storageType = "blob";
        if (value.length > 1048576)
          throw new Error("Database blob exceeds 1 MiB");
        value = {
          type: "blob",
          base64: value.toString("base64"),
          byteLength: value.length,
        };
      } else if (typeof value === "bigint") {
        storageType = "integer";
        value =
          value <= BigInt(Number.MAX_SAFE_INTEGER) &&
          value >= BigInt(Number.MIN_SAFE_INTEGER)
            ? Number(value)
            : value.toString();
      } else if (typeof value === "number") storageType = "real";
      else if (typeof value === "string") {
        storageType = "text";
        if (Buffer.byteLength(value) > 1048576)
          throw new Error("Database text exceeds 1 MiB");
      }
      return {
        columnKey: columnMetadata[index].key,
        columnName: columnMetadata[index].name,
        storageType,
        value,
      };
    });
    bytes += Buffer.byteLength(JSON.stringify(cells));
    if (bytes > 4 * 1024 * 1024) {
      truncated = true;
      break;
    }
    rows.push(Object.fromEntries(cells.map((c) => [c.columnKey, c.value])));
    rowValues.push(cells);
  }
  return {
    columns: columnMetadata.map((c) => c.name),
    columnMetadata,
    rows,
    rowValues,
    truncated,
  };
}
process.once("message", (message) => {
  let db, response;
  try {
    const Database = require(message.modulePath);
    db = new Database(message.databasePath, {
      readonly: true,
      fileMustExist: true,
      timeout: 500,
    });
    db.pragma("query_only = ON");
    db.pragma("trusted_schema = OFF");
    db.pragma("hard_heap_limit = 67108864");
    const { operation, args, databasePath } = message;
    let result;
    if (operation === "query")
      result = {
        databasePath,
        sql: args.sql,
        ...query(db, args.sql, args.maxRows),
      };
    else {
      const tables = db
        .prepare(
          "SELECT name,type,sql FROM sqlite_schema WHERE type IN ('table','view') AND (? IS NULL OR name=?) ORDER BY name LIMIT 501",
        )
        .all(args.table ?? null, args.table ?? null);
      if (tables.length > 500)
        throw new Error("Schema exceeds 500 tables; select a table");
      for (const table of tables) {
        table.columns = db
          .prepare("SELECT * FROM pragma_table_xinfo(?) LIMIT 2001")
          .all(table.name);
        table.indexes = db
          .prepare("SELECT * FROM pragma_index_list(?) LIMIT 2001")
          .all(table.name);
        if (table.columns.length > 2000 || table.indexes.length > 2000)
          throw new Error("Schema metadata exceeds limit");
      }
      result = { databasePath, tables };
    }
    result.capturedAtUtc = new Date().toISOString();
    if (Buffer.byteLength(JSON.stringify(result)) > 8 * 1024 * 1024)
      throw new Error("Database response exceeds 8 MiB");
    response = { result };
  } catch (error) {
    response = { error: error.message };
  } finally {
    db?.close();
  }
  process.send(response, () => process.disconnect());
});
