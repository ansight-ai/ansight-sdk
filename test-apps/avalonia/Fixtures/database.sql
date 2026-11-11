CREATE TABLE benchmark_items (id INTEGER PRIMARY KEY, label TEXT NOT NULL, amount REAL NOT NULL);
INSERT INTO benchmark_items VALUES (101, 'avalonia-fixture', 12.5), (102, 'binary-transfer', 42.25);
PRAGMA user_version = 1;
