import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:path/path.dart' as path;
import 'package:path_provider/path_provider.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:sqflite/sqflite.dart';

enum HarnessTab {
  overview('Overview'),
  navigation('Navigation'),
  data('Data'),
  tools('Tools');

  const HarnessTab(this.title);

  final String title;
}

final class HarnessRoute {
  HarnessRoute(this.name, this.detail);

  String name;
  String detail;

  Map<String, Object?> toJson() => <String, Object?>{
    'name': name,
    'detail': detail,
  };
}

final class HarnessSceneState {
  double rotationSpeed = 46;
  String paletteName = 'studio';
  int lastFrameEpochMs = 0;

  void togglePalette() {
    paletteName = paletteName == 'studio' ? 'thermal' : 'studio';
  }

  Map<String, Object?> toJson() => <String, Object?>{
    'rotationSpeed': rotationSpeed,
    'paletteName': paletteName,
    'lastFrameEpochMs': lastFrameEpochMs,
  };
}

final class HarnessState {
  HarnessTab selectedTab = HarnessTab.overview;
  final List<HarnessRoute> navigationStack = <HarnessRoute>[
    HarnessRoute('Dashboard', 'Initial root route'),
  ];
  final HarnessSceneState scene = HarnessSceneState();
  String? lastInsertedItem;
  int metricButtonTaps = 0;
  int eventButtonTaps = 0;
  int navigationOperations = 0;
  int modalPresentations = 0;
  int modalDismissals = 0;
  int flyoutOpens = 0;
  int customToolInvocations = 0;
  int e2eRuns = 0;
  String? lastCapture;
  String? lastError;

  Map<String, Object?> toJson(HarnessDatabaseSummary database) =>
      <String, Object?>{
        'selectedTab': selectedTab.title,
        'navigationStack': navigationStack
            .map((HarnessRoute route) => route.toJson())
            .toList(growable: false),
        'scene': scene.toJson(),
        'data': <String, Object?>{'lastInsertedItem': lastInsertedItem},
        'metricButtonTaps': metricButtonTaps,
        'eventButtonTaps': eventButtonTaps,
        'navigationOperations': navigationOperations,
        'modalPresentations': modalPresentations,
        'modalDismissals': modalDismissals,
        'flyoutOpens': flyoutOpens,
        'customToolInvocations': customToolInvocations,
        'e2eRuns': e2eRuns,
        'lastCapture': lastCapture,
        'lastError': lastError,
        'database': database.toJson(),
      };
}

final class HarnessDatabaseSummary {
  const HarnessDatabaseSummary({
    required this.name,
    required this.path,
    required this.itemCount,
    required this.eventCount,
    required this.latestItem,
    required this.fixtureFilePath,
  });

  final String name;
  final String path;
  final int itemCount;
  final int eventCount;
  final String? latestItem;
  final String fixtureFilePath;

  Map<String, Object?> toJson() => <String, Object?>{
    'name': name,
    'path': path,
    'itemCount': itemCount,
    'eventCount': eventCount,
    'latestItem': latestItem,
    'fixtureFilePath': fixtureFilePath,
  };
}

final class HarnessFixtureStore {
  static const String databaseName = 'ansight_harness.db';
  static const String fixtureFileName = 'ansight-harness-state.json';

  Database? _database;
  File? _fixtureFile;

  Future<void> initialize() async {
    await _openDatabase();
    await seedIfNeeded();
    final documentsDirectory = await getApplicationDocumentsDirectory();
    _fixtureFile = File(path.join(documentsDirectory.path, fixtureFileName));
    final preferences = await SharedPreferences.getInstance();
    await preferences.setString('ansight.harness.mode', 'sdk-validation');
    await preferences.setInt('ansight.harness.schemaVersion', 1);
    await preferences.setBool('ansight.harness.ready', true);
  }

  Future<void> seedIfNeeded() async {
    final database = await _openDatabase();
    if (Sqflite.firstIntValue(
          await database.rawQuery('SELECT COUNT(*) FROM harness_items'),
        ) ==
        0) {
      await seed();
    }
  }

  Future<void> seed() async {
    final database = await _openDatabase();
    await database.transaction((Transaction transaction) async {
      await transaction.delete('harness_events');
      await transaction.delete('harness_items');
      final now = DateTime.now().millisecondsSinceEpoch;
      const labels = <String>[
        'Alpha order',
        'Beta invoice',
        'Gamma session',
        'Delta profile',
      ];
      for (var index = 0; index < labels.length; index += 1) {
        await transaction.insert('harness_items', <String, Object?>{
          'label': labels[index],
          'kind': index.isEven ? 'order' : 'profile',
          'quantity': index + 1,
          'created_at': now - index * 60000,
        });
      }
      await transaction.insert('harness_events', <String, Object?>{
        'label': 'database.seed',
        'severity': 'info',
        'created_at': now,
      });
    });
  }

  Future<String> insertGeneratedItem() async {
    final database = await _openDatabase();
    final now = DateTime.now().millisecondsSinceEpoch;
    final label = 'Generated item ${now % 100000}';
    await database.transaction((Transaction transaction) async {
      await transaction.insert('harness_items', <String, Object?>{
        'label': label,
        'kind': 'generated',
        'quantity': now % 7 + 1,
        'created_at': now,
      });
      await transaction.insert('harness_events', <String, Object?>{
        'label': 'database.insert',
        'severity': 'info',
        'created_at': now,
      });
    });
    return label;
  }

  Future<HarnessDatabaseSummary> summary() async {
    final database = await _openDatabase();
    final items =
        Sqflite.firstIntValue(
          await database.rawQuery('SELECT COUNT(*) FROM harness_items'),
        ) ??
        0;
    final events =
        Sqflite.firstIntValue(
          await database.rawQuery('SELECT COUNT(*) FROM harness_events'),
        ) ??
        0;
    final latest = await database.rawQuery(
      'SELECT label FROM harness_items ORDER BY id DESC LIMIT 1',
    );
    return HarnessDatabaseSummary(
      name: databaseName,
      path: database.path,
      itemCount: items,
      eventCount: events,
      latestItem: latest.isEmpty ? null : latest.first['label']?.toString(),
      fixtureFilePath: _fixtureFile?.path ?? '',
    );
  }

  Future<File> writeStateFixture(Map<String, Object?> state) async {
    if (_fixtureFile == null) {
      final documentsDirectory = await getApplicationDocumentsDirectory();
      _fixtureFile = File(path.join(documentsDirectory.path, fixtureFileName));
    }
    return _fixtureFile!.writeAsString(
      const JsonEncoder.withIndent('  ').convert(state),
      flush: true,
    );
  }

  Uint8List createBinaryFixture() =>
      Uint8List.fromList(List<int>.generate(4096, (int index) => index % 251));

  Future<Database> _openDatabase() async {
    if (_database != null) {
      return _database!;
    }
    final databasePath = path.join(await getDatabasesPath(), databaseName);
    _database = await openDatabase(
      databasePath,
      version: 1,
      onCreate: (Database database, int version) async {
        await database.execute('''
          CREATE TABLE harness_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            label TEXT NOT NULL,
            kind TEXT NOT NULL,
            quantity INTEGER NOT NULL,
            created_at INTEGER NOT NULL
          )
        ''');
        await database.execute('''
          CREATE TABLE harness_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            label TEXT NOT NULL,
            severity TEXT NOT NULL,
            created_at INTEGER NOT NULL
          )
        ''');
      },
    );
    return _database!;
  }
}
