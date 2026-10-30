import 'dart:io';

import 'package:firebase_core/firebase_core.dart';

/// Build-time settings, passed with `--dart-define` (see README.md). Nothing secret lives here:
/// Firebase client values are public identifiers, and the API is protected by sign-in + App Check.
abstract final class AppConfig {
  /// Base URL of the Arks Scanner API, ending in `/api`.
  /// The Android emulator reaches the host PC's localhost through 10.0.2.2.
  static const apiBaseUrl = String.fromEnvironment('API_BASE_URL', defaultValue: 'http://10.0.2.2:5093/api');

  /// Website origin; its public `/assets/fonts/*.ttf` are used for the text editor's live preview.
  static const webBaseUrl = String.fromEnvironment('WEB_BASE_URL', defaultValue: 'http://10.0.2.2:4200');

  /// Firebase app IDs from Firebase Console > Project settings > Your apps.
  static const firebaseAndroidAppId = String.fromEnvironment('FIREBASE_ANDROID_APP_ID');
  static const firebaseIosAppId = String.fromEnvironment('FIREBASE_IOS_APP_ID');
  static const iosBundleId = 'com.arksoft.arksscanner';
  static const firebaseApiKey =
      String.fromEnvironment('FIREBASE_API_KEY', defaultValue: 'AIzaSyBnXleQbbtwGrgYL4_ww6yH1mgWp-1Atgg');
  static const firebaseProjectId = String.fromEnvironment('FIREBASE_PROJECT_ID', defaultValue: 'superscanner-dev');
  static const firebaseSenderId = String.fromEnvironment('FIREBASE_SENDER_ID', defaultValue: '122650027029');
  static const firebaseStorageBucket =
      String.fromEnvironment('FIREBASE_STORAGE_BUCKET', defaultValue: 'superscanner-dev.firebasestorage.app');

  /// Debug builds use the App Check debug provider; register the token it logs in Firebase Console.
  /// Passing a token also switches release test builds (sideloaded iPhone builds, which cannot run
  /// in debug mode) to the debug provider; store builds must leave it empty.
  static const appCheckDebugToken = String.fromEnvironment('APP_CHECK_DEBUG_TOKEN');

  static String get firebaseAppId => Platform.isIOS ? firebaseIosAppId : firebaseAndroidAppId;

  static bool get firebaseConfigured => firebaseAppId.isNotEmpty;

  static FirebaseOptions get firebaseOptions => FirebaseOptions(
        apiKey: firebaseApiKey,
        appId: firebaseAppId,
        messagingSenderId: firebaseSenderId,
        projectId: firebaseProjectId,
        storageBucket: firebaseStorageBucket,
        iosBundleId: iosBundleId,
      );
}
