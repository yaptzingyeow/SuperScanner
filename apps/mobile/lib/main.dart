import 'dart:io';

import 'package:firebase_app_check/firebase_app_check.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:path_provider/path_provider.dart';
import 'package:provider/provider.dart';

import 'api/api_client.dart';
import 'api/edit_api.dart';
import 'config.dart';
import 'l10n/app_localizations.dart';
import 'services/auth_service.dart';
import 'services/export_service.dart';
import 'services/font_service.dart';
import 'services/local_store.dart';
import 'services/locale_controller.dart';
import 'services/session_state.dart';
import 'services/upload_service.dart';
import 'ui/common.dart';
import 'ui/document_screen.dart';
import 'ui/home_screen.dart';
import 'ui/local_document_screen.dart';
import 'ui/login_screen.dart';
import 'ui/page_screen.dart';
import 'ui/search_screen.dart';
import 'ui/settings_screen.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  if (!AppConfig.firebaseConfigured) {
    runApp(const SetupNeededApp());
    return;
  }
  await Firebase.initializeApp(options: AppConfig.firebaseOptions);
  final debugToken = AppConfig.appCheckDebugToken.isEmpty ? null : AppConfig.appCheckDebugToken;
  final debugAppCheck = kDebugMode || debugToken != null;
  await FirebaseAppCheck.instance.activate(
    providerAndroid:
        debugAppCheck ? AndroidDebugProvider(debugToken: debugToken) : const AndroidPlayIntegrityProvider(),
    providerApple: debugAppCheck
        ? AppleDebugProvider(debugToken: debugToken)
        : const AppleAppAttestWithDeviceCheckFallbackProvider(),
  );
  final support = (await getApplicationSupportDirectory()).path;
  final store = LocalStore(Directory('$support/documents'));
  await store.load();
  final locales = LocaleController(File('$support/settings/locale'));
  await locales.load();
  final auth = AuthService(FirebaseAuth.instance, FirebaseAppCheck.instance);
  // X-Time-Zone needs an IANA name (e.g. Asia/Kuala_Lumpur), which Dart does not expose;
  // without it the server uses the account's stored zone.
  final client = ApiClient(baseUrl: AppConfig.apiBaseUrl, tokens: auth);
  final documents = DocumentsApi(client);
  final edits = EditApi(client);
  runApp(MultiProvider(
    providers: [
      ChangeNotifierProvider.value(value: auth),
      ChangeNotifierProvider.value(value: store),
      ChangeNotifierProvider.value(value: locales),
      Provider.value(value: documents),
      Provider.value(value: edits),
      Provider(create: (_) => FontService(edits, AppConfig.webBaseUrl)),
      Provider(create: (_) => UploadService(documents)),
      Provider(create: (_) => ExportService(documents, store, watermarkFile: File('$support/settings/watermark.json'))),
      ChangeNotifierProvider(create: (_) => SessionState(documents)),
    ],
    child: const ArksScannerApp(),
  ));
}

ThemeData _theme() => ThemeData(
      colorScheme: ColorScheme.fromSeed(seedColor: Brand.green, primary: Brand.green),
      useMaterial3: true,
    );

class ArksScannerApp extends StatefulWidget {
  const ArksScannerApp({super.key});

  @override
  State<ArksScannerApp> createState() => _ArksScannerAppState();
}

class _ArksScannerAppState extends State<ArksScannerApp> {
  late final AuthService _auth = context.read<AuthService>();
  late final GoRouter _router = GoRouter(
    refreshListenable: _auth,
    redirect: (context, state) {
      if (!_auth.isReady) return null;
      final atLogin = state.matchedLocation == '/login';
      if (!_auth.isSignedIn) return atLogin ? null : '/login';
      return atLogin ? '/' : null;
    },
    routes: [
      GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
      GoRoute(path: '/', builder: (_, _) => const _SignedInGate(child: HomeScreen())),
      GoRoute(path: '/settings', builder: (_, _) => const SettingsScreen()),
      GoRoute(path: '/local/:id', builder: (_, state) => LocalDocumentScreen(documentId: state.pathParameters['id']!)),
      GoRoute(
        path: '/documents/:id',
        builder: (_, state) => DocumentScreen(documentId: state.pathParameters['id']!),
        routes: [
          GoRoute(path: 'search', builder: (_, state) => SearchScreen(documentId: state.pathParameters['id']!)),
          GoRoute(
            path: 'pages/:pageId',
            builder: (_, state) =>
                PageScreen(documentId: state.pathParameters['id']!, pageId: state.pathParameters['pageId']!),
          ),
        ],
      ),
    ],
  );

  @override
  Widget build(BuildContext context) => MaterialApp.router(
        title: 'Arks Scanner',
        theme: _theme(),
        routerConfig: _router,
        debugShowCheckedModeBanner: false,
        locale: context.watch<LocaleController>().locale,
        supportedLocales: LocaleController.supported,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        localeResolutionCallback: (locale, _) => LocaleController.resolve(locale == null ? null : [locale]),
      );
}

/// Loads the plan after sign-in and asks for privacy consent once, like the web app.
class _SignedInGate extends StatefulWidget {
  const _SignedInGate({required this.child});

  final Widget child;

  @override
  State<_SignedInGate> createState() => _SignedInGateState();
}

class _SignedInGateState extends State<_SignedInGate> {
  bool _asked = false;

  @override
  void initState() {
    super.initState();
    context.read<SessionState>().refresh();
  }

  @override
  Widget build(BuildContext context) {
    final session = context.watch<SessionState>();
    if (session.plan?.needsConsent == true && !_asked) {
      _asked = true;
      WidgetsBinding.instance.addPostFrameCallback((_) => _askConsent());
    }
    return widget.child;
  }

  Future<void> _askConsent() async {
    final accepted = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (context) => AlertDialog(
        title: Text(context.l.privacyTitle),
        content: Text(context.l.privacyBody),
        actions: [FilledButton(onPressed: () => Navigator.pop(context, true), child: Text(context.l.iUnderstand))],
      ),
    );
    if (accepted == true && mounted) {
      try {
        await context.read<SessionState>().acceptPrivacyNotice();
      } catch (error) {
        if (mounted) showMessage(context, friendlyError(context, error));
      }
    }
  }
}

/// Shown when the app was built without Firebase settings (see apps/mobile/README.md).
class SetupNeededApp extends StatelessWidget {
  const SetupNeededApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
        theme: _theme(),
        home: const Scaffold(
          body: Center(
            child: Padding(
              padding: EdgeInsets.all(24),
              child: Text('This build has no Firebase settings.\n\n'
                  'Build with --dart-define=FIREBASE_ANDROID_APP_ID=… or FIREBASE_IOS_APP_ID=… '
                  '(see apps/mobile/README.md).',
                  textAlign: TextAlign.center),
            ),
          ),
        ),
      );
}
