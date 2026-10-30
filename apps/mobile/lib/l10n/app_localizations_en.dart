// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appName => 'Arks Scanner';

  @override
  String get tagline =>
      'Scan, clean up and sign documents. Private by default.';

  @override
  String get continueWithGoogle => 'Continue with Google';

  @override
  String get or => 'or';

  @override
  String get email => 'Email';

  @override
  String get password => 'Password';

  @override
  String get createAccount => 'Create account';

  @override
  String get signInWithEmail => 'Sign in with email';

  @override
  String get haveAccount => 'I already have an account';

  @override
  String get createAnAccount => 'Create an account';

  @override
  String get forgotPassword => 'Forgot password?';

  @override
  String get enterEmailFirst => 'Enter your email first.';

  @override
  String get resetSent => 'Check your email for a reset link.';

  @override
  String get authIncorrect => 'Email or password is incorrect.';

  @override
  String get authEmailInUse =>
      'An account already uses this email. Sign in instead.';

  @override
  String get authWeakPassword => 'Use a password with at least 6 characters.';

  @override
  String get authInvalidEmail => 'Enter a valid email address.';

  @override
  String get authNoConnection =>
      'No connection. Check your internet and try again.';

  @override
  String get authCancelled => 'Sign-in was cancelled.';

  @override
  String get authFailed => 'Sign-in did not finish. Please try again.';

  @override
  String get myDocuments => 'My documents';

  @override
  String get settingsPrivacy => 'Settings & privacy';

  @override
  String get signOut => 'Sign out';

  @override
  String get signOutQuestion => 'Sign out?';

  @override
  String signOutWarning(int count) {
    return '$count document(s) saved only on this phone will be deleted. Share or save any you want to keep first.';
  }

  @override
  String get cancel => 'Cancel';

  @override
  String get save => 'Save';

  @override
  String get delete => 'Delete';

  @override
  String get rename => 'Rename';

  @override
  String get apply => 'Apply';

  @override
  String get done => 'Done';

  @override
  String get scan => 'Scan';

  @override
  String get importPhotosPdf => 'Import photos or PDF';

  @override
  String get uploadingPages => 'Uploading your pages…';

  @override
  String get onThisPhone => 'On this phone';

  @override
  String get inYourAccount => 'In your account';

  @override
  String get inProgress => 'In progress';

  @override
  String get noDocuments => 'No documents yet. Tap Scan to start.';

  @override
  String pagesAndDate(int count, String date) {
    return '$count page(s) · $date';
  }

  @override
  String get document => 'Document';

  @override
  String get processingPages =>
      'Processing pages… you can start editing finished ones.';

  @override
  String pageNumber(int number) {
    return 'Page $number';
  }

  @override
  String get pageFailed => 'Could not process this page';

  @override
  String get ready => 'Ready';

  @override
  String get processing => 'Processing…';

  @override
  String get removePage => 'Remove page';

  @override
  String get add => 'Add';

  @override
  String get share => 'Share';

  @override
  String get print => 'Print';

  @override
  String get saveCopy => 'Save copy';

  @override
  String get saveToPhone => 'Save to phone';

  @override
  String get savedToPhone => 'Saved to this phone.';

  @override
  String get savedToPhoneFree =>
      'Saved to this phone. The cloud copy will be removed.';

  @override
  String get page => 'Page';

  @override
  String get rotateLeft => 'Rotate left';

  @override
  String get rotateRight => 'Rotate right';

  @override
  String get corners => 'Corners';

  @override
  String get autoDetect => 'Auto detect';

  @override
  String get adjustCorners => 'Adjust corners';

  @override
  String get lookMagic => 'Magic scan';

  @override
  String get lookOriginal => 'Original';

  @override
  String get lookDocument => 'Document';

  @override
  String get lookBright => 'Bright';

  @override
  String get lookRemoveShadows => 'No shadow';

  @override
  String get lookGrayscale => 'Grayscale';

  @override
  String get lookBlackAndWhite => 'Black & white';

  @override
  String get noLongerOnPhone => 'This document is no longer on this phone.';

  @override
  String get deleteFromPhone => 'Delete from this phone?';

  @override
  String get cannotUndo => 'This cannot be undone.';

  @override
  String swipeForMore(int count) {
    return '$count page(s) · swipe to see more';
  }

  @override
  String get editAgain => 'Edit again';

  @override
  String get signedIn => 'Signed in';

  @override
  String get proPlan => 'Pro plan';

  @override
  String get freePlan => 'Free plan';

  @override
  String get proPlanDetail =>
      'Documents are kept in your account and shown on the website.';

  @override
  String get freePlanDetail =>
      'Finished documents are saved on this phone; the cloud copy is temporary.';

  @override
  String get language => 'Language';

  @override
  String get phoneLanguage => 'Same as phone';

  @override
  String get downloadMyData => 'Download my data';

  @override
  String get myDataSubject => 'My Arks Scanner data';

  @override
  String get deleteMyAccount => 'Delete my account';

  @override
  String get deleteAccountQuestion => 'Delete your account?';

  @override
  String get deleteAccountWarning =>
      'Your account, cloud documents and documents on this phone will be deleted. This cannot be undone. Type DELETE to confirm.';

  @override
  String get deleteAccount => 'Delete account';

  @override
  String get privacyTitle => 'Your privacy';

  @override
  String get privacyBody =>
      'Arks Scanner processes your documents privately to scan, clean up and export them. Read the full privacy notice on the Arks Scanner website.';

  @override
  String get iUnderstand => 'I understand';

  @override
  String get errLimit =>
      'You\'ve reached today\'s free limit. Upgrade to Pro or try again tomorrow.';

  @override
  String get errType =>
      'This file type is not supported. Use JPEG, PNG, HEIC or PDF.';

  @override
  String get errTooLarge => 'This file is larger than 25 MB.';

  @override
  String get errEmpty => 'This file is empty.';

  @override
  String get errUpload =>
      'The upload did not finish. Check your connection and try again.';

  @override
  String get errExportTimeout =>
      'The PDF is taking longer than expected. Try again in a moment.';

  @override
  String get errSignedOut => 'Please sign in again.';

  @override
  String get errAppCheck =>
      'This app could not be verified. Update the app and try again.';

  @override
  String get errServer => 'Something went wrong on our side. Please try again.';

  @override
  String get errGeneric => 'Something went wrong. Please try again.';

  @override
  String get exportOptions => 'Export options';

  @override
  String get addWatermark => 'Add a watermark';

  @override
  String get watermarkSubtitle =>
      'Stamp every page, e.g. “For government use only”.';

  @override
  String get wmText => 'Text';

  @override
  String get wmQuickText => 'Quick text';

  @override
  String get wmOnce => 'Once';

  @override
  String get wmRepeated => 'Repeated';

  @override
  String get wmPosition => 'Position';

  @override
  String get wmTop => 'Top';

  @override
  String get wmCenter => 'Center';

  @override
  String get wmBottom => 'Bottom';

  @override
  String get wmFont => 'Font';

  @override
  String get wmBold => 'Bold';

  @override
  String get wmColour => 'Colour';

  @override
  String get wmTransparency => 'Transparency';

  @override
  String get wmSize => 'Size';

  @override
  String get wmAngle => 'Angle';

  @override
  String get wmSpacing => 'Spacing';

  @override
  String get wmReset => 'Reset to default';

  @override
  String get wmPreviewNote =>
      'Preview of page 1. The PDF uses the chosen font.';

  @override
  String get wmPresetGovernment => 'FOR GOVERNMENT USE ONLY';

  @override
  String get wmPresetIc => 'FOR IC VERIFICATION ONLY';

  @override
  String get wmPresetConfidential => 'CONFIDENTIAL';

  @override
  String get wmPresetCopy => 'COPY';

  @override
  String get wmPresetDraft => 'DRAFT';

  @override
  String get wmPresetSample => 'SAMPLE';

  @override
  String get errWatermark =>
      'This watermark can\'t be used. Use 1–120 characters of plain text.';

  @override
  String get errNoConnection =>
      'Can\'t reach the server. Check your internet connection and try again.';

  @override
  String get errForbidden =>
      'The server refused this request. Sign out and in again; if it continues, this app build may not be verified.';

  @override
  String get errNotFound =>
      'This document or page no longer exists. Go back and refresh.';

  @override
  String get errRejected =>
      'The server couldn\'t accept this change. Try a different selection or setting.';

  @override
  String get errTooMany =>
      'Too many requests at once. Wait a moment and try again.';

  @override
  String errorCode(String detail) {
    return 'Error code: $detail';
  }

  @override
  String get errOverflow =>
      'The text does not fit. Shorten it or make the text smaller.';

  @override
  String get errUnsafeBackground =>
      'This area has a pattern or photo behind the text, so it cannot be replaced cleanly.';

  @override
  String get errOverlap =>
      'The new text would cover other text. Move it or make it smaller.';

  @override
  String get errStale =>
      'The page changed. It has been reloaded; please try again.';

  @override
  String get errFontUnsupported =>
      'This font cannot write these characters. Choose another font.';

  @override
  String get errTextDisabled => 'Text editing is turned off right now.';

  @override
  String get errOcrDisabled => 'Text recognition is turned off right now.';

  @override
  String get errPageNotReady =>
      'This page is still processing. Try again in a moment.';

  @override
  String get errRepairFailed =>
      'The eraser could not clean this area. Try a smaller area.';

  @override
  String get errSignatureNoInk =>
      'No signature was found in this photo. Use a clear photo of dark ink on light paper.';

  @override
  String get errSignatureImage =>
      'This image cannot be used. Use a PNG or JPEG under 5 MB.';

  @override
  String get search => 'Search';

  @override
  String get searchPlaceholder => 'Search recognized text';

  @override
  String pagesNotRecognised(int count) {
    return '$count page(s) have no recognized text yet.';
  }

  @override
  String get noMatches => 'No matches.';

  @override
  String matchCount(int count) {
    return '$count match(es)';
  }

  @override
  String get preview => 'Preview';

  @override
  String get size => 'Size';

  @override
  String get clear => 'Clear';

  @override
  String get undo => 'Undo';

  @override
  String get redo => 'Redo';

  @override
  String get toolEraser => 'Eraser';

  @override
  String get undoStroke => 'Undo last area';

  @override
  String get eraserHint =>
      'Paint over punch holes, staples or specks. Only the painted areas change.';

  @override
  String get findHoles => 'Find holes';

  @override
  String get noHolesFound => 'No punch holes found.';

  @override
  String holesFound(int count) {
    return 'Found $count hole(s). Preview to remove them.';
  }

  @override
  String get brushSmall => 'Small';

  @override
  String get brushMedium => 'Medium';

  @override
  String get brushLarge => 'Large';

  @override
  String get showBefore => 'Show before';

  @override
  String get toolMarks => 'Tick / Cross';

  @override
  String get toolMarksShort => 'Tick';

  @override
  String get marksHint =>
      'Tap a box on the form to place a mark. Tap a mark to move, resize or delete it.';

  @override
  String get tick => 'Tick';

  @override
  String get cross => 'Cross';

  @override
  String get toolSign => 'Add a signature';

  @override
  String get toolSignShort => 'Sign';

  @override
  String get signHint =>
      'Draw a signature or use a photo of one, then drag it into place. This is a visual signature, not a digital certificate.';

  @override
  String get drawSignature => 'Draw';

  @override
  String get signaturePhoto => 'From photo';

  @override
  String get toolText => 'Text';

  @override
  String get replaceText => 'Replace';

  @override
  String get addText => 'Add text';

  @override
  String get recogniseText => 'Recognize text';

  @override
  String get recognising => 'Recognizing text…';

  @override
  String get recogniseFirst => 'Recognize the text on this page first.';

  @override
  String get recogniseFailed => 'Text recognition did not finish. Try again.';

  @override
  String get noTextFound => 'No text was found on this page.';

  @override
  String get replaceHint => 'Tap the words to replace. Pinch to zoom in.';

  @override
  String get addTextHint => 'Tap where the new text should go.';

  @override
  String editSelectedWords(int count) {
    return 'Edit $count word(s)';
  }

  @override
  String originalText(String text) {
    return 'Original: $text';
  }

  @override
  String get replacementText => 'Replacement text';

  @override
  String get newText => 'New text';

  @override
  String get moreOptions => 'More options';

  @override
  String get selectOneLine =>
      'Only neighbouring words on one line can be replaced together, so a new selection was started.';

  @override
  String get errSelectionInvalid => 'Choose neighbouring words on one line.';

  @override
  String get errSelectionUnsupported =>
      'Handwritten text can\'t be replaced. Choose printed words.';

  @override
  String get hideEditor => 'Hide editor';

  @override
  String get showEditor => 'Show editor';

  @override
  String get panelTransparency => 'Panel transparency';

  @override
  String get moveTextHint =>
      'Drag the box to move the text, or drag its corner to resize. Tapping the page also moves it.';

  @override
  String get font => 'Font';

  @override
  String get weight => 'Weight';

  @override
  String get strikethrough => 'Strikethrough';

  @override
  String get letterSpacing => 'Spacing';

  @override
  String get fontSans => 'Sans-serif';

  @override
  String get fontSerif => 'Serif';

  @override
  String get fontMono => 'Monospace';

  @override
  String get fontHandwriting => 'Handwriting';

  @override
  String get exactResult => 'Exact result';
}
