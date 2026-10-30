import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_ar.dart';
import 'app_localizations_en.dart';
import 'app_localizations_ms.dart';
import 'app_localizations_zh.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
    : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations)!;
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
        delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
      ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('ar'),
    Locale('en'),
    Locale('ms'),
    Locale('zh'),
  ];

  /// No description provided for @appName.
  ///
  /// In en, this message translates to:
  /// **'Arks Scanner'**
  String get appName;

  /// No description provided for @tagline.
  ///
  /// In en, this message translates to:
  /// **'Scan, clean up and sign documents. Private by default.'**
  String get tagline;

  /// No description provided for @continueWithGoogle.
  ///
  /// In en, this message translates to:
  /// **'Continue with Google'**
  String get continueWithGoogle;

  /// No description provided for @or.
  ///
  /// In en, this message translates to:
  /// **'or'**
  String get or;

  /// No description provided for @email.
  ///
  /// In en, this message translates to:
  /// **'Email'**
  String get email;

  /// No description provided for @password.
  ///
  /// In en, this message translates to:
  /// **'Password'**
  String get password;

  /// No description provided for @createAccount.
  ///
  /// In en, this message translates to:
  /// **'Create account'**
  String get createAccount;

  /// No description provided for @signInWithEmail.
  ///
  /// In en, this message translates to:
  /// **'Sign in with email'**
  String get signInWithEmail;

  /// No description provided for @haveAccount.
  ///
  /// In en, this message translates to:
  /// **'I already have an account'**
  String get haveAccount;

  /// No description provided for @createAnAccount.
  ///
  /// In en, this message translates to:
  /// **'Create an account'**
  String get createAnAccount;

  /// No description provided for @forgotPassword.
  ///
  /// In en, this message translates to:
  /// **'Forgot password?'**
  String get forgotPassword;

  /// No description provided for @enterEmailFirst.
  ///
  /// In en, this message translates to:
  /// **'Enter your email first.'**
  String get enterEmailFirst;

  /// No description provided for @resetSent.
  ///
  /// In en, this message translates to:
  /// **'Check your email for a reset link.'**
  String get resetSent;

  /// No description provided for @authIncorrect.
  ///
  /// In en, this message translates to:
  /// **'Email or password is incorrect.'**
  String get authIncorrect;

  /// No description provided for @authEmailInUse.
  ///
  /// In en, this message translates to:
  /// **'An account already uses this email. Sign in instead.'**
  String get authEmailInUse;

  /// No description provided for @authWeakPassword.
  ///
  /// In en, this message translates to:
  /// **'Use a password with at least 6 characters.'**
  String get authWeakPassword;

  /// No description provided for @authInvalidEmail.
  ///
  /// In en, this message translates to:
  /// **'Enter a valid email address.'**
  String get authInvalidEmail;

  /// No description provided for @authNoConnection.
  ///
  /// In en, this message translates to:
  /// **'No connection. Check your internet and try again.'**
  String get authNoConnection;

  /// No description provided for @authCancelled.
  ///
  /// In en, this message translates to:
  /// **'Sign-in was cancelled.'**
  String get authCancelled;

  /// No description provided for @authFailed.
  ///
  /// In en, this message translates to:
  /// **'Sign-in did not finish. Please try again.'**
  String get authFailed;

  /// No description provided for @myDocuments.
  ///
  /// In en, this message translates to:
  /// **'My documents'**
  String get myDocuments;

  /// No description provided for @settingsPrivacy.
  ///
  /// In en, this message translates to:
  /// **'Settings & privacy'**
  String get settingsPrivacy;

  /// No description provided for @signOut.
  ///
  /// In en, this message translates to:
  /// **'Sign out'**
  String get signOut;

  /// No description provided for @signOutQuestion.
  ///
  /// In en, this message translates to:
  /// **'Sign out?'**
  String get signOutQuestion;

  /// No description provided for @signOutWarning.
  ///
  /// In en, this message translates to:
  /// **'{count} document(s) saved only on this phone will be deleted. Share or save any you want to keep first.'**
  String signOutWarning(int count);

  /// No description provided for @cancel.
  ///
  /// In en, this message translates to:
  /// **'Cancel'**
  String get cancel;

  /// No description provided for @save.
  ///
  /// In en, this message translates to:
  /// **'Save'**
  String get save;

  /// No description provided for @delete.
  ///
  /// In en, this message translates to:
  /// **'Delete'**
  String get delete;

  /// No description provided for @rename.
  ///
  /// In en, this message translates to:
  /// **'Rename'**
  String get rename;

  /// No description provided for @apply.
  ///
  /// In en, this message translates to:
  /// **'Apply'**
  String get apply;

  /// No description provided for @done.
  ///
  /// In en, this message translates to:
  /// **'Done'**
  String get done;

  /// No description provided for @scan.
  ///
  /// In en, this message translates to:
  /// **'Scan'**
  String get scan;

  /// No description provided for @importPhotosPdf.
  ///
  /// In en, this message translates to:
  /// **'Import photos or PDF'**
  String get importPhotosPdf;

  /// No description provided for @uploadingPages.
  ///
  /// In en, this message translates to:
  /// **'Uploading your pages…'**
  String get uploadingPages;

  /// No description provided for @onThisPhone.
  ///
  /// In en, this message translates to:
  /// **'On this phone'**
  String get onThisPhone;

  /// No description provided for @inYourAccount.
  ///
  /// In en, this message translates to:
  /// **'In your account'**
  String get inYourAccount;

  /// No description provided for @inProgress.
  ///
  /// In en, this message translates to:
  /// **'In progress'**
  String get inProgress;

  /// No description provided for @noDocuments.
  ///
  /// In en, this message translates to:
  /// **'No documents yet. Tap Scan to start.'**
  String get noDocuments;

  /// No description provided for @pagesAndDate.
  ///
  /// In en, this message translates to:
  /// **'{count} page(s) · {date}'**
  String pagesAndDate(int count, String date);

  /// No description provided for @document.
  ///
  /// In en, this message translates to:
  /// **'Document'**
  String get document;

  /// No description provided for @processingPages.
  ///
  /// In en, this message translates to:
  /// **'Processing pages… you can start editing finished ones.'**
  String get processingPages;

  /// No description provided for @pageNumber.
  ///
  /// In en, this message translates to:
  /// **'Page {number}'**
  String pageNumber(int number);

  /// No description provided for @pageFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not process this page'**
  String get pageFailed;

  /// No description provided for @ready.
  ///
  /// In en, this message translates to:
  /// **'Ready'**
  String get ready;

  /// No description provided for @processing.
  ///
  /// In en, this message translates to:
  /// **'Processing…'**
  String get processing;

  /// No description provided for @removePage.
  ///
  /// In en, this message translates to:
  /// **'Remove page'**
  String get removePage;

  /// No description provided for @add.
  ///
  /// In en, this message translates to:
  /// **'Add'**
  String get add;

  /// No description provided for @share.
  ///
  /// In en, this message translates to:
  /// **'Share'**
  String get share;

  /// No description provided for @print.
  ///
  /// In en, this message translates to:
  /// **'Print'**
  String get print;

  /// No description provided for @saveCopy.
  ///
  /// In en, this message translates to:
  /// **'Save copy'**
  String get saveCopy;

  /// No description provided for @saveToPhone.
  ///
  /// In en, this message translates to:
  /// **'Save to phone'**
  String get saveToPhone;

  /// No description provided for @savedToPhone.
  ///
  /// In en, this message translates to:
  /// **'Saved to this phone.'**
  String get savedToPhone;

  /// No description provided for @savedToPhoneFree.
  ///
  /// In en, this message translates to:
  /// **'Saved to this phone. The cloud copy will be removed.'**
  String get savedToPhoneFree;

  /// No description provided for @page.
  ///
  /// In en, this message translates to:
  /// **'Page'**
  String get page;

  /// No description provided for @rotateLeft.
  ///
  /// In en, this message translates to:
  /// **'Rotate left'**
  String get rotateLeft;

  /// No description provided for @rotateRight.
  ///
  /// In en, this message translates to:
  /// **'Rotate right'**
  String get rotateRight;

  /// No description provided for @corners.
  ///
  /// In en, this message translates to:
  /// **'Corners'**
  String get corners;

  /// No description provided for @autoDetect.
  ///
  /// In en, this message translates to:
  /// **'Auto detect'**
  String get autoDetect;

  /// No description provided for @adjustCorners.
  ///
  /// In en, this message translates to:
  /// **'Adjust corners'**
  String get adjustCorners;

  /// No description provided for @lookMagic.
  ///
  /// In en, this message translates to:
  /// **'Magic scan'**
  String get lookMagic;

  /// No description provided for @lookOriginal.
  ///
  /// In en, this message translates to:
  /// **'Original'**
  String get lookOriginal;

  /// No description provided for @lookDocument.
  ///
  /// In en, this message translates to:
  /// **'Document'**
  String get lookDocument;

  /// No description provided for @lookBright.
  ///
  /// In en, this message translates to:
  /// **'Bright'**
  String get lookBright;

  /// No description provided for @lookRemoveShadows.
  ///
  /// In en, this message translates to:
  /// **'No shadow'**
  String get lookRemoveShadows;

  /// No description provided for @lookGrayscale.
  ///
  /// In en, this message translates to:
  /// **'Grayscale'**
  String get lookGrayscale;

  /// No description provided for @lookBlackAndWhite.
  ///
  /// In en, this message translates to:
  /// **'Black & white'**
  String get lookBlackAndWhite;

  /// No description provided for @noLongerOnPhone.
  ///
  /// In en, this message translates to:
  /// **'This document is no longer on this phone.'**
  String get noLongerOnPhone;

  /// No description provided for @deleteFromPhone.
  ///
  /// In en, this message translates to:
  /// **'Delete from this phone?'**
  String get deleteFromPhone;

  /// No description provided for @cannotUndo.
  ///
  /// In en, this message translates to:
  /// **'This cannot be undone.'**
  String get cannotUndo;

  /// No description provided for @swipeForMore.
  ///
  /// In en, this message translates to:
  /// **'{count} page(s) · swipe to see more'**
  String swipeForMore(int count);

  /// No description provided for @editAgain.
  ///
  /// In en, this message translates to:
  /// **'Edit again'**
  String get editAgain;

  /// No description provided for @signedIn.
  ///
  /// In en, this message translates to:
  /// **'Signed in'**
  String get signedIn;

  /// No description provided for @proPlan.
  ///
  /// In en, this message translates to:
  /// **'Pro plan'**
  String get proPlan;

  /// No description provided for @freePlan.
  ///
  /// In en, this message translates to:
  /// **'Free plan'**
  String get freePlan;

  /// No description provided for @proPlanDetail.
  ///
  /// In en, this message translates to:
  /// **'Documents are kept in your account and shown on the website.'**
  String get proPlanDetail;

  /// No description provided for @freePlanDetail.
  ///
  /// In en, this message translates to:
  /// **'Finished documents are saved on this phone; the cloud copy is temporary.'**
  String get freePlanDetail;

  /// No description provided for @language.
  ///
  /// In en, this message translates to:
  /// **'Language'**
  String get language;

  /// No description provided for @phoneLanguage.
  ///
  /// In en, this message translates to:
  /// **'Same as phone'**
  String get phoneLanguage;

  /// No description provided for @downloadMyData.
  ///
  /// In en, this message translates to:
  /// **'Download my data'**
  String get downloadMyData;

  /// No description provided for @myDataSubject.
  ///
  /// In en, this message translates to:
  /// **'My Arks Scanner data'**
  String get myDataSubject;

  /// No description provided for @deleteMyAccount.
  ///
  /// In en, this message translates to:
  /// **'Delete my account'**
  String get deleteMyAccount;

  /// No description provided for @deleteAccountQuestion.
  ///
  /// In en, this message translates to:
  /// **'Delete your account?'**
  String get deleteAccountQuestion;

  /// No description provided for @deleteAccountWarning.
  ///
  /// In en, this message translates to:
  /// **'Your account, cloud documents and documents on this phone will be deleted. This cannot be undone. Type DELETE to confirm.'**
  String get deleteAccountWarning;

  /// No description provided for @deleteAccount.
  ///
  /// In en, this message translates to:
  /// **'Delete account'**
  String get deleteAccount;

  /// No description provided for @privacyTitle.
  ///
  /// In en, this message translates to:
  /// **'Your privacy'**
  String get privacyTitle;

  /// No description provided for @privacyBody.
  ///
  /// In en, this message translates to:
  /// **'Arks Scanner processes your documents privately to scan, clean up and export them. Read the full privacy notice on the Arks Scanner website.'**
  String get privacyBody;

  /// No description provided for @iUnderstand.
  ///
  /// In en, this message translates to:
  /// **'I understand'**
  String get iUnderstand;

  /// No description provided for @errLimit.
  ///
  /// In en, this message translates to:
  /// **'You\'ve reached today\'s free limit. Upgrade to Pro or try again tomorrow.'**
  String get errLimit;

  /// No description provided for @errType.
  ///
  /// In en, this message translates to:
  /// **'This file type is not supported. Use JPEG, PNG, HEIC or PDF.'**
  String get errType;

  /// No description provided for @errTooLarge.
  ///
  /// In en, this message translates to:
  /// **'This file is larger than 25 MB.'**
  String get errTooLarge;

  /// No description provided for @errEmpty.
  ///
  /// In en, this message translates to:
  /// **'This file is empty.'**
  String get errEmpty;

  /// No description provided for @errUpload.
  ///
  /// In en, this message translates to:
  /// **'The upload did not finish. Check your connection and try again.'**
  String get errUpload;

  /// No description provided for @errExportTimeout.
  ///
  /// In en, this message translates to:
  /// **'The PDF is taking longer than expected. Try again in a moment.'**
  String get errExportTimeout;

  /// No description provided for @errSignedOut.
  ///
  /// In en, this message translates to:
  /// **'Please sign in again.'**
  String get errSignedOut;

  /// No description provided for @errAppCheck.
  ///
  /// In en, this message translates to:
  /// **'This app could not be verified. Update the app and try again.'**
  String get errAppCheck;

  /// No description provided for @errServer.
  ///
  /// In en, this message translates to:
  /// **'Something went wrong on our side. Please try again.'**
  String get errServer;

  /// No description provided for @errGeneric.
  ///
  /// In en, this message translates to:
  /// **'Something went wrong. Please try again.'**
  String get errGeneric;

  /// No description provided for @errOverflow.
  ///
  /// In en, this message translates to:
  /// **'The text does not fit. Shorten it or make the text smaller.'**
  String get errOverflow;

  /// No description provided for @errUnsafeBackground.
  ///
  /// In en, this message translates to:
  /// **'This area has a pattern or photo behind the text, so it cannot be replaced cleanly.'**
  String get errUnsafeBackground;

  /// No description provided for @errOverlap.
  ///
  /// In en, this message translates to:
  /// **'The new text would cover other text. Move it or make it smaller.'**
  String get errOverlap;

  /// No description provided for @errStale.
  ///
  /// In en, this message translates to:
  /// **'The page changed. It has been reloaded; please try again.'**
  String get errStale;

  /// No description provided for @errFontUnsupported.
  ///
  /// In en, this message translates to:
  /// **'This font cannot write these characters. Choose another font.'**
  String get errFontUnsupported;

  /// No description provided for @errTextDisabled.
  ///
  /// In en, this message translates to:
  /// **'Text editing is turned off right now.'**
  String get errTextDisabled;

  /// No description provided for @errOcrDisabled.
  ///
  /// In en, this message translates to:
  /// **'Text recognition is turned off right now.'**
  String get errOcrDisabled;

  /// No description provided for @errPageNotReady.
  ///
  /// In en, this message translates to:
  /// **'This page is still processing. Try again in a moment.'**
  String get errPageNotReady;

  /// No description provided for @errRepairFailed.
  ///
  /// In en, this message translates to:
  /// **'The eraser could not clean this area. Try a smaller area.'**
  String get errRepairFailed;

  /// No description provided for @errSignatureNoInk.
  ///
  /// In en, this message translates to:
  /// **'No signature was found in this photo. Use a clear photo of dark ink on light paper.'**
  String get errSignatureNoInk;

  /// No description provided for @errSignatureImage.
  ///
  /// In en, this message translates to:
  /// **'This image cannot be used. Use a PNG or JPEG under 5 MB.'**
  String get errSignatureImage;

  /// No description provided for @search.
  ///
  /// In en, this message translates to:
  /// **'Search'**
  String get search;

  /// No description provided for @searchPlaceholder.
  ///
  /// In en, this message translates to:
  /// **'Search recognized text'**
  String get searchPlaceholder;

  /// No description provided for @pagesNotRecognised.
  ///
  /// In en, this message translates to:
  /// **'{count} page(s) have no recognized text yet.'**
  String pagesNotRecognised(int count);

  /// No description provided for @noMatches.
  ///
  /// In en, this message translates to:
  /// **'No matches.'**
  String get noMatches;

  /// No description provided for @matchCount.
  ///
  /// In en, this message translates to:
  /// **'{count} match(es)'**
  String matchCount(int count);

  /// No description provided for @preview.
  ///
  /// In en, this message translates to:
  /// **'Preview'**
  String get preview;

  /// No description provided for @size.
  ///
  /// In en, this message translates to:
  /// **'Size'**
  String get size;

  /// No description provided for @clear.
  ///
  /// In en, this message translates to:
  /// **'Clear'**
  String get clear;

  /// No description provided for @undo.
  ///
  /// In en, this message translates to:
  /// **'Undo'**
  String get undo;

  /// No description provided for @redo.
  ///
  /// In en, this message translates to:
  /// **'Redo'**
  String get redo;

  /// No description provided for @toolEraser.
  ///
  /// In en, this message translates to:
  /// **'Eraser'**
  String get toolEraser;

  /// No description provided for @undoStroke.
  ///
  /// In en, this message translates to:
  /// **'Undo last area'**
  String get undoStroke;

  /// No description provided for @eraserHint.
  ///
  /// In en, this message translates to:
  /// **'Paint over punch holes, staples or specks. Only the painted areas change.'**
  String get eraserHint;

  /// No description provided for @findHoles.
  ///
  /// In en, this message translates to:
  /// **'Find holes'**
  String get findHoles;

  /// No description provided for @noHolesFound.
  ///
  /// In en, this message translates to:
  /// **'No punch holes found.'**
  String get noHolesFound;

  /// No description provided for @holesFound.
  ///
  /// In en, this message translates to:
  /// **'Found {count} hole(s). Preview to remove them.'**
  String holesFound(int count);

  /// No description provided for @brushSmall.
  ///
  /// In en, this message translates to:
  /// **'Small'**
  String get brushSmall;

  /// No description provided for @brushMedium.
  ///
  /// In en, this message translates to:
  /// **'Medium'**
  String get brushMedium;

  /// No description provided for @brushLarge.
  ///
  /// In en, this message translates to:
  /// **'Large'**
  String get brushLarge;

  /// No description provided for @showBefore.
  ///
  /// In en, this message translates to:
  /// **'Show before'**
  String get showBefore;

  /// No description provided for @toolMarks.
  ///
  /// In en, this message translates to:
  /// **'Tick / Cross'**
  String get toolMarks;

  /// No description provided for @toolMarksShort.
  ///
  /// In en, this message translates to:
  /// **'Tick'**
  String get toolMarksShort;

  /// No description provided for @marksHint.
  ///
  /// In en, this message translates to:
  /// **'Tap a box on the form to place a mark. Tap a mark to move, resize or delete it.'**
  String get marksHint;

  /// No description provided for @tick.
  ///
  /// In en, this message translates to:
  /// **'Tick'**
  String get tick;

  /// No description provided for @cross.
  ///
  /// In en, this message translates to:
  /// **'Cross'**
  String get cross;

  /// No description provided for @toolSign.
  ///
  /// In en, this message translates to:
  /// **'Add a signature'**
  String get toolSign;

  /// No description provided for @toolSignShort.
  ///
  /// In en, this message translates to:
  /// **'Sign'**
  String get toolSignShort;

  /// No description provided for @signHint.
  ///
  /// In en, this message translates to:
  /// **'Draw a signature or use a photo of one, then drag it into place. This is a visual signature, not a digital certificate.'**
  String get signHint;

  /// No description provided for @drawSignature.
  ///
  /// In en, this message translates to:
  /// **'Draw'**
  String get drawSignature;

  /// No description provided for @signaturePhoto.
  ///
  /// In en, this message translates to:
  /// **'From photo'**
  String get signaturePhoto;

  /// No description provided for @toolText.
  ///
  /// In en, this message translates to:
  /// **'Text'**
  String get toolText;

  /// No description provided for @replaceText.
  ///
  /// In en, this message translates to:
  /// **'Replace'**
  String get replaceText;

  /// No description provided for @addText.
  ///
  /// In en, this message translates to:
  /// **'Add text'**
  String get addText;

  /// No description provided for @recogniseText.
  ///
  /// In en, this message translates to:
  /// **'Recognize text'**
  String get recogniseText;

  /// No description provided for @recognising.
  ///
  /// In en, this message translates to:
  /// **'Recognizing text…'**
  String get recognising;

  /// No description provided for @recogniseFirst.
  ///
  /// In en, this message translates to:
  /// **'Recognize the text on this page first.'**
  String get recogniseFirst;

  /// No description provided for @recogniseFailed.
  ///
  /// In en, this message translates to:
  /// **'Text recognition did not finish. Try again.'**
  String get recogniseFailed;

  /// No description provided for @noTextFound.
  ///
  /// In en, this message translates to:
  /// **'No text was found on this page.'**
  String get noTextFound;

  /// No description provided for @replaceHint.
  ///
  /// In en, this message translates to:
  /// **'Tap the words to replace. Pinch to zoom in.'**
  String get replaceHint;

  /// No description provided for @addTextHint.
  ///
  /// In en, this message translates to:
  /// **'Tap where the new text should go.'**
  String get addTextHint;

  /// No description provided for @editSelectedWords.
  ///
  /// In en, this message translates to:
  /// **'Edit {count} word(s)'**
  String editSelectedWords(int count);

  /// No description provided for @originalText.
  ///
  /// In en, this message translates to:
  /// **'Original: {text}'**
  String originalText(String text);

  /// No description provided for @replacementText.
  ///
  /// In en, this message translates to:
  /// **'Replacement text'**
  String get replacementText;

  /// No description provided for @newText.
  ///
  /// In en, this message translates to:
  /// **'New text'**
  String get newText;

  /// No description provided for @moreOptions.
  ///
  /// In en, this message translates to:
  /// **'More options'**
  String get moreOptions;

  /// No description provided for @selectOneLine.
  ///
  /// In en, this message translates to:
  /// **'Only neighbouring words on one line can be replaced together, so a new selection was started.'**
  String get selectOneLine;

  /// No description provided for @errSelectionInvalid.
  ///
  /// In en, this message translates to:
  /// **'Choose neighbouring words on one line.'**
  String get errSelectionInvalid;

  /// No description provided for @errSelectionUnsupported.
  ///
  /// In en, this message translates to:
  /// **'Handwritten text can\'t be replaced. Choose printed words.'**
  String get errSelectionUnsupported;

  /// No description provided for @hideEditor.
  ///
  /// In en, this message translates to:
  /// **'Hide editor'**
  String get hideEditor;

  /// No description provided for @showEditor.
  ///
  /// In en, this message translates to:
  /// **'Show editor'**
  String get showEditor;

  /// No description provided for @panelTransparency.
  ///
  /// In en, this message translates to:
  /// **'Panel transparency'**
  String get panelTransparency;

  /// No description provided for @moveTextHint.
  ///
  /// In en, this message translates to:
  /// **'Drag the box to move the text, or drag its corner to resize. Tapping the page also moves it.'**
  String get moveTextHint;

  /// No description provided for @font.
  ///
  /// In en, this message translates to:
  /// **'Font'**
  String get font;

  /// No description provided for @weight.
  ///
  /// In en, this message translates to:
  /// **'Weight'**
  String get weight;

  /// No description provided for @strikethrough.
  ///
  /// In en, this message translates to:
  /// **'Strikethrough'**
  String get strikethrough;

  /// No description provided for @letterSpacing.
  ///
  /// In en, this message translates to:
  /// **'Spacing'**
  String get letterSpacing;

  /// No description provided for @fontSans.
  ///
  /// In en, this message translates to:
  /// **'Sans-serif'**
  String get fontSans;

  /// No description provided for @fontSerif.
  ///
  /// In en, this message translates to:
  /// **'Serif'**
  String get fontSerif;

  /// No description provided for @fontMono.
  ///
  /// In en, this message translates to:
  /// **'Monospace'**
  String get fontMono;

  /// No description provided for @fontHandwriting.
  ///
  /// In en, this message translates to:
  /// **'Handwriting'**
  String get fontHandwriting;

  /// No description provided for @exactResult.
  ///
  /// In en, this message translates to:
  /// **'Exact result'**
  String get exactResult;
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['ar', 'en', 'ms', 'zh'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'ar':
      return AppLocalizationsAr();
    case 'en':
      return AppLocalizationsEn();
    case 'ms':
      return AppLocalizationsMs();
    case 'zh':
      return AppLocalizationsZh();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.',
  );
}
