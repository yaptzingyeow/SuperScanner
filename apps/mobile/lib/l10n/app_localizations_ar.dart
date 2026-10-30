// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Arabic (`ar`).
class AppLocalizationsAr extends AppLocalizations {
  AppLocalizationsAr([String locale = 'ar']) : super(locale);

  @override
  String get appName => 'Arks Scanner';

  @override
  String get tagline => 'امسح المستندات ونظّفها ووقّعها. خاصة افتراضيًا.';

  @override
  String get continueWithGoogle => 'المتابعة باستخدام Google';

  @override
  String get or => 'أو';

  @override
  String get email => 'البريد الإلكتروني';

  @override
  String get password => 'كلمة المرور';

  @override
  String get createAccount => 'إنشاء حساب';

  @override
  String get signInWithEmail => 'تسجيل الدخول بالبريد الإلكتروني';

  @override
  String get haveAccount => 'لديّ حساب بالفعل';

  @override
  String get createAnAccount => 'إنشاء حساب جديد';

  @override
  String get forgotPassword => 'نسيت كلمة المرور؟';

  @override
  String get enterEmailFirst => 'أدخل بريدك الإلكتروني أولًا.';

  @override
  String get resetSent =>
      'تحقق من بريدك الإلكتروني للحصول على رابط إعادة التعيين.';

  @override
  String get authIncorrect => 'البريد الإلكتروني أو كلمة المرور غير صحيحة.';

  @override
  String get authEmailInUse =>
      'هذا البريد مستخدم بالفعل. سجّل الدخول بدلًا من ذلك.';

  @override
  String get authWeakPassword => 'استخدم كلمة مرور من 6 أحرف على الأقل.';

  @override
  String get authInvalidEmail => 'أدخل عنوان بريد إلكتروني صالحًا.';

  @override
  String get authNoConnection =>
      'لا يوجد اتصال. تحقق من الإنترنت وحاول مرة أخرى.';

  @override
  String get authCancelled => 'تم إلغاء تسجيل الدخول.';

  @override
  String get authFailed => 'لم يكتمل تسجيل الدخول. حاول مرة أخرى.';

  @override
  String get myDocuments => 'مستنداتي';

  @override
  String get settingsPrivacy => 'الإعدادات والخصوصية';

  @override
  String get signOut => 'تسجيل الخروج';

  @override
  String get signOutQuestion => 'تسجيل الخروج؟';

  @override
  String signOutWarning(int count) {
    return 'سيتم حذف $count من المستندات المحفوظة على هذا الهاتف فقط. شارك أو احفظ ما تريد الاحتفاظ به أولًا.';
  }

  @override
  String get cancel => 'إلغاء';

  @override
  String get save => 'حفظ';

  @override
  String get delete => 'حذف';

  @override
  String get rename => 'إعادة التسمية';

  @override
  String get apply => 'تطبيق';

  @override
  String get done => 'تم';

  @override
  String get scan => 'مسح';

  @override
  String get importPhotosPdf => 'استيراد صور أو PDF';

  @override
  String get uploadingPages => 'جارٍ رفع صفحاتك…';

  @override
  String get onThisPhone => 'على هذا الهاتف';

  @override
  String get inYourAccount => 'في حسابك';

  @override
  String get inProgress => 'قيد التحرير';

  @override
  String get noDocuments => 'لا توجد مستندات بعد. اضغط \"مسح\" للبدء.';

  @override
  String pagesAndDate(int count, String date) {
    return '$count صفحة · $date';
  }

  @override
  String get document => 'مستند';

  @override
  String get processingPages =>
      'جارٍ معالجة الصفحات… يمكنك بدء تحرير الصفحات الجاهزة.';

  @override
  String pageNumber(int number) {
    return 'الصفحة $number';
  }

  @override
  String get pageFailed => 'تعذّرت معالجة هذه الصفحة';

  @override
  String get ready => 'جاهزة';

  @override
  String get processing => 'جارٍ المعالجة…';

  @override
  String get removePage => 'إزالة الصفحة';

  @override
  String get add => 'إضافة';

  @override
  String get share => 'مشاركة';

  @override
  String get print => 'طباعة';

  @override
  String get saveCopy => 'حفظ نسخة';

  @override
  String get saveToPhone => 'حفظ على الهاتف';

  @override
  String get savedToPhone => 'تم الحفظ على هذا الهاتف.';

  @override
  String get savedToPhoneFree =>
      'تم الحفظ على هذا الهاتف. ستتم إزالة النسخة السحابية.';

  @override
  String get page => 'الصفحة';

  @override
  String get rotateLeft => 'تدوير لليسار';

  @override
  String get rotateRight => 'تدوير لليمين';

  @override
  String get corners => 'الزوايا';

  @override
  String get autoDetect => 'كشف تلقائي';

  @override
  String get adjustCorners => 'ضبط الزوايا';

  @override
  String get lookMagic => 'المسح السحري';

  @override
  String get lookOriginal => 'الأصلي';

  @override
  String get lookDocument => 'مستند';

  @override
  String get lookBright => 'ساطع';

  @override
  String get lookRemoveShadows => 'بلا ظل';

  @override
  String get lookGrayscale => 'تدرّج رمادي';

  @override
  String get lookBlackAndWhite => 'أبيض وأسود';

  @override
  String get noLongerOnPhone => 'لم يعد هذا المستند على هذا الهاتف.';

  @override
  String get deleteFromPhone => 'الحذف من هذا الهاتف؟';

  @override
  String get cannotUndo => 'لا يمكن التراجع عن ذلك.';

  @override
  String swipeForMore(int count) {
    return '$count صفحة · اسحب لرؤية المزيد';
  }

  @override
  String get editAgain => 'تحرير مجددًا';

  @override
  String get signedIn => 'تم تسجيل الدخول';

  @override
  String get proPlan => 'خطة Pro';

  @override
  String get freePlan => 'الخطة المجانية';

  @override
  String get proPlanDetail => 'تُحفظ المستندات في حسابك وتظهر على الموقع.';

  @override
  String get freePlanDetail =>
      'تُحفظ المستندات المكتملة على هذا الهاتف؛ والنسخة السحابية مؤقتة.';

  @override
  String get language => 'اللغة';

  @override
  String get phoneLanguage => 'مثل الهاتف';

  @override
  String get downloadMyData => 'تنزيل بياناتي';

  @override
  String get myDataSubject => 'بياناتي في Arks Scanner';

  @override
  String get deleteMyAccount => 'حذف حسابي';

  @override
  String get deleteAccountQuestion => 'حذف حسابك؟';

  @override
  String get deleteAccountWarning =>
      'سيتم حذف حسابك ومستنداتك السحابية والمستندات على هذا الهاتف. لا يمكن التراجع عن ذلك. اكتب DELETE للتأكيد.';

  @override
  String get deleteAccount => 'حذف الحساب';

  @override
  String get privacyTitle => 'خصوصيتك';

  @override
  String get privacyBody =>
      'يعالج Arks Scanner مستنداتك بشكل خاص لمسحها وتنظيفها وتصديرها. اقرأ إشعار الخصوصية الكامل على موقع Arks Scanner.';

  @override
  String get iUnderstand => 'فهمت';

  @override
  String get errLimit =>
      'لقد وصلت إلى الحد المجاني لليوم. قم بالترقية إلى Pro أو حاول غدًا.';

  @override
  String get errType =>
      'نوع الملف غير مدعوم. استخدم JPEG أو PNG أو HEIC أو PDF.';

  @override
  String get errTooLarge => 'هذا الملف أكبر من 25 ميغابايت.';

  @override
  String get errEmpty => 'هذا الملف فارغ.';

  @override
  String get errUpload => 'لم يكتمل الرفع. تحقق من اتصالك وحاول مرة أخرى.';

  @override
  String get errExportTimeout =>
      'يستغرق ملف PDF وقتًا أطول من المتوقع. حاول بعد قليل.';

  @override
  String get errSignedOut => 'يرجى تسجيل الدخول مرة أخرى.';

  @override
  String get errAppCheck =>
      'تعذّر التحقق من هذا التطبيق. حدّث التطبيق وحاول مرة أخرى.';

  @override
  String get errServer => 'حدث خطأ من جهتنا. حاول مرة أخرى.';

  @override
  String get errGeneric => 'حدث خطأ ما. حاول مرة أخرى.';

  @override
  String get errNoConnection =>
      'تعذر الوصول إلى الخادم. تحقق من اتصال الإنترنت وحاول مرة أخرى.';

  @override
  String get errForbidden =>
      'رفض الخادم هذا الطلب. سجّل الخروج ثم الدخول مرة أخرى؛ وإذا استمر ذلك فقد لا يكون إصدار التطبيق هذا موثّقًا.';

  @override
  String get errNotFound =>
      'لم يعد هذا المستند أو الصفحة موجودًا. ارجع وحدّث الصفحة.';

  @override
  String get errRejected =>
      'تعذر على الخادم قبول هذا التغيير. جرّب تحديدًا أو إعدادًا آخر.';

  @override
  String get errTooMany =>
      'طلبات كثيرة في الوقت نفسه. انتظر قليلًا وحاول مرة أخرى.';

  @override
  String errorCode(String detail) {
    return 'رمز الخطأ: $detail';
  }

  @override
  String get errOverflow => 'النص لا يتسع. اختصره أو صغّر حجمه.';

  @override
  String get errUnsafeBackground =>
      'توجد خلف النص في هذه المنطقة نقوش أو صورة، لذا لا يمكن استبداله بشكل نظيف.';

  @override
  String get errOverlap => 'سيغطي النص الجديد نصًا آخر. انقله أو صغّره.';

  @override
  String get errStale => 'تغيّرت الصفحة وأُعيد تحميلها؛ حاول مرة أخرى.';

  @override
  String get errFontUnsupported =>
      'لا يدعم هذا الخط هذه الأحرف. اختر خطًا آخر.';

  @override
  String get errTextDisabled => 'تحرير النص متوقف حاليًا.';

  @override
  String get errOcrDisabled => 'التعرّف على النص متوقف حاليًا.';

  @override
  String get errPageNotReady =>
      'لا تزال هذه الصفحة قيد المعالجة. حاول بعد قليل.';

  @override
  String get errRepairFailed =>
      'تعذّر على الممحاة تنظيف هذه المنطقة. جرّب منطقة أصغر.';

  @override
  String get errSignatureNoInk =>
      'لم يُعثر على توقيع في هذه الصورة. استخدم صورة واضحة لحبر داكن على ورق فاتح.';

  @override
  String get errSignatureImage =>
      'لا يمكن استخدام هذه الصورة. استخدم PNG أو JPEG أقل من 5 ميغابايت.';

  @override
  String get search => 'بحث';

  @override
  String get searchPlaceholder => 'ابحث في النص المعرَّف';

  @override
  String pagesNotRecognised(int count) {
    return '$count صفحة لم يُتعرَّف على نصها بعد.';
  }

  @override
  String get noMatches => 'لا توجد نتائج.';

  @override
  String matchCount(int count) {
    return '$count نتيجة';
  }

  @override
  String get preview => 'معاينة';

  @override
  String get size => 'الحجم';

  @override
  String get clear => 'مسح';

  @override
  String get undo => 'تراجع';

  @override
  String get redo => 'إعادة';

  @override
  String get toolEraser => 'الممحاة';

  @override
  String get undoStroke => 'التراجع عن آخر منطقة';

  @override
  String get eraserHint =>
      'لوّن فوق ثقوب التخريم أو الدبابيس أو البقع. تتغير المناطق الملوّنة فقط.';

  @override
  String get findHoles => 'البحث عن الثقوب';

  @override
  String get noHolesFound => 'لم يُعثر على ثقوب تخريم.';

  @override
  String holesFound(int count) {
    return 'عُثر على $count ثقب. عاين لإزالتها.';
  }

  @override
  String get brushSmall => 'صغير';

  @override
  String get brushMedium => 'متوسط';

  @override
  String get brushLarge => 'كبير';

  @override
  String get showBefore => 'عرض ما قبل';

  @override
  String get toolMarks => 'علامة صح / خطأ';

  @override
  String get toolMarksShort => 'علامة';

  @override
  String get marksHint =>
      'اضغط على مربع في النموذج لوضع علامة. اضغط على علامة لنقلها أو تغيير حجمها أو حذفها.';

  @override
  String get tick => 'صح';

  @override
  String get cross => 'خطأ';

  @override
  String get toolSign => 'إضافة توقيع';

  @override
  String get toolSignShort => 'توقيع';

  @override
  String get signHint =>
      'ارسم توقيعًا أو استخدم صورة له، ثم اسحبه إلى مكانه. هذا توقيع مرئي وليس شهادة رقمية.';

  @override
  String get drawSignature => 'رسم';

  @override
  String get signaturePhoto => 'من صورة';

  @override
  String get toolText => 'نص';

  @override
  String get replaceText => 'استبدال';

  @override
  String get addText => 'إضافة نص';

  @override
  String get recogniseText => 'التعرّف على النص';

  @override
  String get recognising => 'جارٍ التعرّف على النص…';

  @override
  String get recogniseFirst => 'تعرّف على نص هذه الصفحة أولًا.';

  @override
  String get recogniseFailed => 'لم يكتمل التعرّف على النص. حاول مرة أخرى.';

  @override
  String get noTextFound => 'لم يُعثر على نص في هذه الصفحة.';

  @override
  String get replaceHint =>
      'اضغط على الكلمات المراد استبدالها. باعد إصبعيك للتكبير.';

  @override
  String get addTextHint => 'اضغط حيث يجب أن يظهر النص الجديد.';

  @override
  String editSelectedWords(int count) {
    return 'تحرير $count كلمة';
  }

  @override
  String originalText(String text) {
    return 'الأصلي: $text';
  }

  @override
  String get replacementText => 'النص البديل';

  @override
  String get newText => 'نص جديد';

  @override
  String get moreOptions => 'خيارات أخرى';

  @override
  String get selectOneLine =>
      'يمكن استبدال الكلمات المتجاورة في سطر واحد فقط معًا، لذلك بدأ تحديد جديد.';

  @override
  String get errSelectionInvalid => 'اختر كلمات متجاورة في سطر واحد.';

  @override
  String get errSelectionUnsupported =>
      'لا يمكن استبدال النص المكتوب بخط اليد. اختر كلمات مطبوعة.';

  @override
  String get hideEditor => 'إخفاء المحرر';

  @override
  String get showEditor => 'إظهار المحرر';

  @override
  String get panelTransparency => 'شفافية اللوحة';

  @override
  String get moveTextHint =>
      'اسحب المربع لتحريك النص، أو اسحب زاويته لتغيير الحجم. النقر على الصفحة يحرّكه أيضًا.';

  @override
  String get font => 'الخط';

  @override
  String get weight => 'السُمك';

  @override
  String get strikethrough => 'يتوسطه خط';

  @override
  String get letterSpacing => 'التباعد';

  @override
  String get fontSans => 'بدون تذييل';

  @override
  String get fontSerif => 'بتذييل';

  @override
  String get fontMono => 'ثابت العرض';

  @override
  String get fontHandwriting => 'خط اليد';

  @override
  String get exactResult => 'النتيجة الفعلية';
}
