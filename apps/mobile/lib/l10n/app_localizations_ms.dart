// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Malay (`ms`).
class AppLocalizationsMs extends AppLocalizations {
  AppLocalizationsMs([String locale = 'ms']) : super(locale);

  @override
  String get appName => 'Arks Scanner';

  @override
  String get tagline =>
      'Imbas, kemas dan tandatangan dokumen. Peribadi secara lalai.';

  @override
  String get continueWithGoogle => 'Teruskan dengan Google';

  @override
  String get or => 'atau';

  @override
  String get email => 'E-mel';

  @override
  String get password => 'Kata laluan';

  @override
  String get createAccount => 'Cipta akaun';

  @override
  String get signInWithEmail => 'Log masuk dengan e-mel';

  @override
  String get haveAccount => 'Saya sudah mempunyai akaun';

  @override
  String get createAnAccount => 'Cipta akaun baharu';

  @override
  String get forgotPassword => 'Lupa kata laluan?';

  @override
  String get enterEmailFirst => 'Masukkan e-mel anda dahulu.';

  @override
  String get resetSent => 'Semak e-mel anda untuk pautan tetapan semula.';

  @override
  String get authIncorrect => 'E-mel atau kata laluan tidak betul.';

  @override
  String get authEmailInUse => 'E-mel ini sudah digunakan. Sila log masuk.';

  @override
  String get authWeakPassword =>
      'Gunakan kata laluan sekurang-kurangnya 6 aksara.';

  @override
  String get authInvalidEmail => 'Masukkan alamat e-mel yang sah.';

  @override
  String get authNoConnection =>
      'Tiada sambungan. Semak internet anda dan cuba lagi.';

  @override
  String get authCancelled => 'Log masuk dibatalkan.';

  @override
  String get authFailed => 'Log masuk tidak selesai. Sila cuba lagi.';

  @override
  String get myDocuments => 'Dokumen saya';

  @override
  String get settingsPrivacy => 'Tetapan & privasi';

  @override
  String get signOut => 'Log keluar';

  @override
  String get signOutQuestion => 'Log keluar?';

  @override
  String signOutWarning(int count) {
    return '$count dokumen yang disimpan di telefon ini sahaja akan dipadam. Kongsi atau simpan yang anda mahu dahulu.';
  }

  @override
  String get cancel => 'Batal';

  @override
  String get save => 'Simpan';

  @override
  String get delete => 'Padam';

  @override
  String get rename => 'Namakan semula';

  @override
  String get apply => 'Guna';

  @override
  String get done => 'Selesai';

  @override
  String get scan => 'Imbas';

  @override
  String get importPhotosPdf => 'Import foto atau PDF';

  @override
  String get uploadingPages => 'Memuat naik halaman anda…';

  @override
  String get onThisPhone => 'Di telefon ini';

  @override
  String get inYourAccount => 'Dalam akaun anda';

  @override
  String get inProgress => 'Sedang disunting';

  @override
  String get noDocuments => 'Belum ada dokumen. Ketik Imbas untuk bermula.';

  @override
  String pagesAndDate(int count, String date) {
    return '$count halaman · $date';
  }

  @override
  String get document => 'Dokumen';

  @override
  String get processingPages =>
      'Memproses halaman… anda boleh mula menyunting yang sudah siap.';

  @override
  String pageNumber(int number) {
    return 'Halaman $number';
  }

  @override
  String get pageFailed => 'Halaman ini tidak dapat diproses';

  @override
  String get ready => 'Sedia';

  @override
  String get processing => 'Memproses…';

  @override
  String get removePage => 'Buang halaman';

  @override
  String get add => 'Tambah';

  @override
  String get share => 'Kongsi';

  @override
  String get print => 'Cetak';

  @override
  String get saveCopy => 'Simpan salinan';

  @override
  String get saveToPhone => 'Simpan ke telefon';

  @override
  String get savedToPhone => 'Disimpan ke telefon ini.';

  @override
  String get savedToPhoneFree =>
      'Disimpan ke telefon ini. Salinan awan akan dibuang.';

  @override
  String get page => 'Halaman';

  @override
  String get rotateLeft => 'Putar ke kiri';

  @override
  String get rotateRight => 'Putar ke kanan';

  @override
  String get corners => 'Sudut';

  @override
  String get autoDetect => 'Kesan automatik';

  @override
  String get adjustCorners => 'Laraskan sudut';

  @override
  String get lookMagic => 'Imbasan ajaib';

  @override
  String get lookOriginal => 'Asal';

  @override
  String get lookDocument => 'Dokumen';

  @override
  String get lookBright => 'Cerah';

  @override
  String get lookRemoveShadows => 'Tanpa bayang';

  @override
  String get lookGrayscale => 'Skala kelabu';

  @override
  String get lookBlackAndWhite => 'Hitam & putih';

  @override
  String get noLongerOnPhone => 'Dokumen ini tiada lagi di telefon ini.';

  @override
  String get deleteFromPhone => 'Padam daripada telefon ini?';

  @override
  String get cannotUndo => 'Tindakan ini tidak boleh dibuat asal.';

  @override
  String swipeForMore(int count) {
    return '$count halaman · leret untuk lihat lagi';
  }

  @override
  String get editAgain => 'Sunting semula';

  @override
  String get signedIn => 'Telah log masuk';

  @override
  String get proPlan => 'Pelan Pro';

  @override
  String get freePlan => 'Pelan Percuma';

  @override
  String get proPlanDetail =>
      'Dokumen disimpan dalam akaun anda dan dipaparkan di laman web.';

  @override
  String get freePlanDetail =>
      'Dokumen siap disimpan di telefon ini; salinan awan adalah sementara.';

  @override
  String get language => 'Bahasa';

  @override
  String get phoneLanguage => 'Sama seperti telefon';

  @override
  String get downloadMyData => 'Muat turun data saya';

  @override
  String get myDataSubject => 'Data Arks Scanner saya';

  @override
  String get deleteMyAccount => 'Padam akaun saya';

  @override
  String get deleteAccountQuestion => 'Padam akaun anda?';

  @override
  String get deleteAccountWarning =>
      'Akaun anda, dokumen awan dan dokumen di telefon ini akan dipadam. Tindakan ini tidak boleh dibuat asal. Taip DELETE untuk mengesahkan.';

  @override
  String get deleteAccount => 'Padam akaun';

  @override
  String get privacyTitle => 'Privasi anda';

  @override
  String get privacyBody =>
      'Arks Scanner memproses dokumen anda secara peribadi untuk mengimbas, mengemas dan mengeksportnya. Baca notis privasi penuh di laman web Arks Scanner.';

  @override
  String get iUnderstand => 'Saya faham';

  @override
  String get errLimit =>
      'Anda telah mencapai had percuma hari ini. Naik taraf ke Pro atau cuba lagi esok.';

  @override
  String get errType =>
      'Jenis fail ini tidak disokong. Gunakan JPEG, PNG, HEIC atau PDF.';

  @override
  String get errTooLarge => 'Fail ini lebih besar daripada 25 MB.';

  @override
  String get errEmpty => 'Fail ini kosong.';

  @override
  String get errUpload =>
      'Muat naik tidak selesai. Semak sambungan anda dan cuba lagi.';

  @override
  String get errExportTimeout =>
      'PDF mengambil masa lebih lama daripada dijangka. Cuba lagi sebentar lagi.';

  @override
  String get errSignedOut => 'Sila log masuk semula.';

  @override
  String get errAppCheck =>
      'Aplikasi ini tidak dapat disahkan. Kemas kini aplikasi dan cuba lagi.';

  @override
  String get errServer => 'Ada masalah di pihak kami. Sila cuba lagi.';

  @override
  String get errGeneric => 'Ada masalah. Sila cuba lagi.';

  @override
  String get errNoConnection =>
      'Tidak dapat menghubungi pelayan. Semak sambungan internet anda dan cuba lagi.';

  @override
  String get errForbidden =>
      'Pelayan menolak permintaan ini. Log keluar dan masuk semula; jika berterusan, binaan aplikasi ini mungkin tidak disahkan.';

  @override
  String get errNotFound =>
      'Dokumen atau halaman ini sudah tiada. Kembali dan muat semula.';

  @override
  String get errRejected =>
      'Pelayan tidak dapat menerima perubahan ini. Cuba pilihan atau tetapan lain.';

  @override
  String get errTooMany =>
      'Terlalu banyak permintaan serentak. Tunggu sebentar dan cuba lagi.';

  @override
  String errorCode(String detail) {
    return 'Kod ralat: $detail';
  }

  @override
  String get errOverflow => 'Teks tidak muat. Pendekkan atau kecilkan teks.';

  @override
  String get errUnsafeBackground =>
      'Kawasan ini mempunyai corak atau foto di belakang teks, jadi tidak boleh diganti dengan kemas.';

  @override
  String get errOverlap =>
      'Teks baharu akan menutup teks lain. Alihkan atau kecilkan.';

  @override
  String get errStale =>
      'Halaman telah berubah dan dimuat semula; sila cuba lagi.';

  @override
  String get errFontUnsupported =>
      'Fon ini tidak boleh menulis aksara ini. Pilih fon lain.';

  @override
  String get errTextDisabled => 'Penyuntingan teks dimatikan buat masa ini.';

  @override
  String get errOcrDisabled => 'Pengecaman teks dimatikan buat masa ini.';

  @override
  String get errPageNotReady =>
      'Halaman ini masih diproses. Cuba lagi sebentar lagi.';

  @override
  String get errRepairFailed =>
      'Pemadam tidak dapat membersihkan kawasan ini. Cuba kawasan yang lebih kecil.';

  @override
  String get errSignatureNoInk =>
      'Tiada tandatangan dijumpai dalam foto ini. Gunakan foto dakwat gelap yang jelas di atas kertas cerah.';

  @override
  String get errSignatureImage =>
      'Imej ini tidak boleh digunakan. Gunakan PNG atau JPEG di bawah 5 MB.';

  @override
  String get search => 'Cari';

  @override
  String get searchPlaceholder => 'Cari teks yang dikenali';

  @override
  String pagesNotRecognised(int count) {
    return '$count halaman belum mempunyai teks yang dikenali.';
  }

  @override
  String get noMatches => 'Tiada padanan.';

  @override
  String matchCount(int count) {
    return '$count padanan';
  }

  @override
  String get preview => 'Pratonton';

  @override
  String get size => 'Saiz';

  @override
  String get clear => 'Kosongkan';

  @override
  String get undo => 'Buat asal';

  @override
  String get redo => 'Buat semula';

  @override
  String get toolEraser => 'Pemadam';

  @override
  String get undoStroke => 'Buat asal kawasan terakhir';

  @override
  String get eraserHint =>
      'Sapu di atas lubang tebuk, staples atau bintik. Hanya kawasan yang disapu akan berubah.';

  @override
  String get findHoles => 'Cari lubang';

  @override
  String get noHolesFound => 'Tiada lubang tebuk dijumpai.';

  @override
  String holesFound(int count) {
    return '$count lubang dijumpai. Pratonton untuk membuangnya.';
  }

  @override
  String get brushSmall => 'Kecil';

  @override
  String get brushMedium => 'Sederhana';

  @override
  String get brushLarge => 'Besar';

  @override
  String get showBefore => 'Tunjuk sebelum';

  @override
  String get toolMarks => 'Tanda / Pangkah';

  @override
  String get toolMarksShort => 'Tanda';

  @override
  String get marksHint =>
      'Ketik kotak pada borang untuk meletakkan tanda. Ketik tanda untuk alih, ubah saiz atau padam.';

  @override
  String get tick => 'Tanda';

  @override
  String get cross => 'Pangkah';

  @override
  String get toolSign => 'Tambah tandatangan';

  @override
  String get toolSignShort => 'Tandatangan';

  @override
  String get signHint =>
      'Lukis tandatangan atau gunakan fotonya, kemudian seret ke tempatnya. Ini tandatangan visual, bukan sijil digital.';

  @override
  String get drawSignature => 'Lukis';

  @override
  String get signaturePhoto => 'Daripada foto';

  @override
  String get toolText => 'Teks';

  @override
  String get replaceText => 'Ganti';

  @override
  String get addText => 'Tambah teks';

  @override
  String get recogniseText => 'Kenal pasti teks';

  @override
  String get recognising => 'Mengenal pasti teks…';

  @override
  String get recogniseFirst => 'Kenal pasti teks pada halaman ini dahulu.';

  @override
  String get recogniseFailed => 'Pengecaman teks tidak selesai. Cuba lagi.';

  @override
  String get noTextFound => 'Tiada teks dijumpai pada halaman ini.';

  @override
  String get replaceHint => 'Ketik perkataan untuk diganti. Cubit untuk zum.';

  @override
  String get addTextHint => 'Ketik tempat teks baharu patut diletakkan.';

  @override
  String editSelectedWords(int count) {
    return 'Sunting $count perkataan';
  }

  @override
  String originalText(String text) {
    return 'Asal: $text';
  }

  @override
  String get replacementText => 'Teks pengganti';

  @override
  String get newText => 'Teks baharu';

  @override
  String get moreOptions => 'Lagi pilihan';

  @override
  String get selectOneLine =>
      'Hanya perkataan bersebelahan pada satu baris boleh diganti bersama, jadi pilihan baharu dimulakan.';

  @override
  String get errSelectionInvalid =>
      'Pilih perkataan bersebelahan pada satu baris.';

  @override
  String get errSelectionUnsupported =>
      'Tulisan tangan tidak boleh diganti. Pilih perkataan bercetak.';

  @override
  String get hideEditor => 'Sembunyikan penyunting';

  @override
  String get showEditor => 'Tunjukkan penyunting';

  @override
  String get panelTransparency => 'Ketelusan panel';

  @override
  String get moveTextHint =>
      'Seret kotak untuk mengalihkan teks, atau seret bucunya untuk ubah saiz. Ketik halaman juga mengalihkannya.';

  @override
  String get font => 'Fon';

  @override
  String get weight => 'Ketebalan';

  @override
  String get strikethrough => 'Garis tengah';

  @override
  String get letterSpacing => 'Jarak';

  @override
  String get fontSans => 'Sans-serif';

  @override
  String get fontSerif => 'Serif';

  @override
  String get fontMono => 'Monospace';

  @override
  String get fontHandwriting => 'Tulisan tangan';

  @override
  String get exactResult => 'Hasil sebenar';
}
