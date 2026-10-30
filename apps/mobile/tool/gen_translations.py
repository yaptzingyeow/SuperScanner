"""Writes lib/l10n/app_{ms,zh,ar}.arb from the translations below, copying placeholder metadata from
app_en.arb and refusing to write if any key is missing or extra. Machine-quality translations:
send them for native-speaker review with docs/translation-review (same process as the web app).

Run: python tool/gen_translations.py
"""
import json
import os

ROOT = os.path.join(os.path.dirname(__file__), '..', 'lib', 'l10n')

T = {
 'appName': ('Arks Scanner', 'Arks Scanner', 'Arks Scanner'),
 'tagline': ('Imbas, kemas dan tandatangan dokumen. Peribadi secara lalai.', '扫描、清理并签署文档。默认保密。', 'امسح المستندات ونظّفها ووقّعها. خاصة افتراضيًا.'),
 'continueWithGoogle': ('Teruskan dengan Google', '使用 Google 继续', 'المتابعة باستخدام Google'),
 'or': ('atau', '或', 'أو'),
 'email': ('E-mel', '电子邮件', 'البريد الإلكتروني'),
 'password': ('Kata laluan', '密码', 'كلمة المرور'),
 'createAccount': ('Cipta akaun', '创建账户', 'إنشاء حساب'),
 'signInWithEmail': ('Log masuk dengan e-mel', '使用电子邮件登录', 'تسجيل الدخول بالبريد الإلكتروني'),
 'haveAccount': ('Saya sudah mempunyai akaun', '我已有账户', 'لديّ حساب بالفعل'),
 'createAnAccount': ('Cipta akaun baharu', '创建新账户', 'إنشاء حساب جديد'),
 'forgotPassword': ('Lupa kata laluan?', '忘记密码？', 'نسيت كلمة المرور؟'),
 'enterEmailFirst': ('Masukkan e-mel anda dahulu.', '请先输入您的电子邮件。', 'أدخل بريدك الإلكتروني أولًا.'),
 'resetSent': ('Semak e-mel anda untuk pautan tetapan semula.', '请查看电子邮件中的重置链接。', 'تحقق من بريدك الإلكتروني للحصول على رابط إعادة التعيين.'),
 'authIncorrect': ('E-mel atau kata laluan tidak betul.', '电子邮件或密码不正确。', 'البريد الإلكتروني أو كلمة المرور غير صحيحة.'),
 'authEmailInUse': ('E-mel ini sudah digunakan. Sila log masuk.', '此电子邮件已被使用，请直接登录。', 'هذا البريد مستخدم بالفعل. سجّل الدخول بدلًا من ذلك.'),
 'authWeakPassword': ('Gunakan kata laluan sekurang-kurangnya 6 aksara.', '请使用至少 6 个字符的密码。', 'استخدم كلمة مرور من 6 أحرف على الأقل.'),
 'authInvalidEmail': ('Masukkan alamat e-mel yang sah.', '请输入有效的电子邮件地址。', 'أدخل عنوان بريد إلكتروني صالحًا.'),
 'authNoConnection': ('Tiada sambungan. Semak internet anda dan cuba lagi.', '无网络连接。请检查网络后重试。', 'لا يوجد اتصال. تحقق من الإنترنت وحاول مرة أخرى.'),
 'authCancelled': ('Log masuk dibatalkan.', '已取消登录。', 'تم إلغاء تسجيل الدخول.'),
 'authFailed': ('Log masuk tidak selesai. Sila cuba lagi.', '登录未完成，请重试。', 'لم يكتمل تسجيل الدخول. حاول مرة أخرى.'),

 'myDocuments': ('Dokumen saya', '我的文档', 'مستنداتي'),
 'settingsPrivacy': ('Tetapan & privasi', '设置与隐私', 'الإعدادات والخصوصية'),
 'signOut': ('Log keluar', '退出登录', 'تسجيل الخروج'),
 'signOutQuestion': ('Log keluar?', '退出登录？', 'تسجيل الخروج؟'),
 'signOutWarning': ('{count} dokumen yang disimpan di telefon ini sahaja akan dipadam. Kongsi atau simpan yang anda mahu dahulu.',
                    '仅保存在此手机上的 {count} 个文档将被删除。请先分享或保存需要保留的文档。',
                    'سيتم حذف {count} من المستندات المحفوظة على هذا الهاتف فقط. شارك أو احفظ ما تريد الاحتفاظ به أولًا.'),
 'cancel': ('Batal', '取消', 'إلغاء'),
 'save': ('Simpan', '保存', 'حفظ'),
 'delete': ('Padam', '删除', 'حذف'),
 'rename': ('Namakan semula', '重命名', 'إعادة التسمية'),
 'apply': ('Guna', '应用', 'تطبيق'),
 'done': ('Selesai', '完成', 'تم'),
 'scan': ('Imbas', '扫描', 'مسح'),
 'importPhotosPdf': ('Import foto atau PDF', '导入照片或 PDF', 'استيراد صور أو PDF'),
 'uploadingPages': ('Memuat naik halaman anda…', '正在上传页面…', 'جارٍ رفع صفحاتك…'),
 'onThisPhone': ('Di telefon ini', '在此手机上', 'على هذا الهاتف'),
 'inYourAccount': ('Dalam akaun anda', '在您的账户中', 'في حسابك'),
 'inProgress': ('Sedang disunting', '编辑中', 'قيد التحرير'),
 'noDocuments': ('Belum ada dokumen. Ketik Imbas untuk bermula.', '还没有文档。点按"扫描"开始。', 'لا توجد مستندات بعد. اضغط "مسح" للبدء.'),
 'pagesAndDate': ('{count} halaman · {date}', '{count} 页 · {date}', '{count} صفحة · {date}'),

 'document': ('Dokumen', '文档', 'مستند'),
 'processingPages': ('Memproses halaman… anda boleh mula menyunting yang sudah siap.', '正在处理页面……可以先编辑已完成的页面。', 'جارٍ معالجة الصفحات… يمكنك بدء تحرير الصفحات الجاهزة.'),
 'pageNumber': ('Halaman {number}', '第 {number} 页', 'الصفحة {number}'),
 'pageFailed': ('Halaman ini tidak dapat diproses', '无法处理此页面', 'تعذّرت معالجة هذه الصفحة'),
 'ready': ('Sedia', '就绪', 'جاهزة'),
 'processing': ('Memproses…', '处理中…', 'جارٍ المعالجة…'),
 'removePage': ('Buang halaman', '删除页面', 'إزالة الصفحة'),
 'add': ('Tambah', '添加', 'إضافة'),
 'share': ('Kongsi', '分享', 'مشاركة'),
 'print': ('Cetak', '打印', 'طباعة'),
 'saveCopy': ('Simpan salinan', '保存副本', 'حفظ نسخة'),
 'saveToPhone': ('Simpan ke telefon', '保存到手机', 'حفظ على الهاتف'),
 'savedToPhone': ('Disimpan ke telefon ini.', '已保存到此手机。', 'تم الحفظ على هذا الهاتف.'),
 'savedToPhoneFree': ('Disimpan ke telefon ini. Salinan awan akan dibuang.', '已保存到此手机。云端副本将被删除。', 'تم الحفظ على هذا الهاتف. ستتم إزالة النسخة السحابية.'),

 'page': ('Halaman', '页面', 'الصفحة'),
 'rotateLeft': ('Putar ke kiri', '向左旋转', 'تدوير لليسار'),
 'rotateRight': ('Putar ke kanan', '向右旋转', 'تدوير لليمين'),
 'corners': ('Sudut', '角点', 'الزوايا'),
 'autoDetect': ('Kesan automatik', '自动检测', 'كشف تلقائي'),
 'adjustCorners': ('Laraskan sudut', '调整角点', 'ضبط الزوايا'),
 'lookMagic': ('Imbasan ajaib', '魔法扫描', 'المسح السحري'),
 'lookOriginal': ('Asal', '原图', 'الأصلي'),
 'lookDocument': ('Dokumen', '文档', 'مستند'),
 'lookBright': ('Cerah', '明亮', 'ساطع'),
 'lookRemoveShadows': ('Tanpa bayang', '去阴影', 'بلا ظل'),
 'lookGrayscale': ('Skala kelabu', '灰度', 'تدرّج رمادي'),
 'lookBlackAndWhite': ('Hitam & putih', '黑白', 'أبيض وأسود'),

 'noLongerOnPhone': ('Dokumen ini tiada lagi di telefon ini.', '此文档已不在此手机上。', 'لم يعد هذا المستند على هذا الهاتف.'),
 'deleteFromPhone': ('Padam daripada telefon ini?', '从此手机中删除？', 'الحذف من هذا الهاتف؟'),
 'cannotUndo': ('Tindakan ini tidak boleh dibuat asal.', '此操作无法撤销。', 'لا يمكن التراجع عن ذلك.'),
 'swipeForMore': ('{count} halaman · leret untuk lihat lagi', '{count} 页 · 滑动查看更多', '{count} صفحة · اسحب لرؤية المزيد'),
 'editAgain': ('Sunting semula', '再次编辑', 'تحرير مجددًا'),

 'signedIn': ('Telah log masuk', '已登录', 'تم تسجيل الدخول'),
 'proPlan': ('Pelan Pro', 'Pro 方案', 'خطة Pro'),
 'freePlan': ('Pelan Percuma', '免费方案', 'الخطة المجانية'),
 'proPlanDetail': ('Dokumen disimpan dalam akaun anda dan dipaparkan di laman web.', '文档保存在您的账户中，并显示在网站上。', 'تُحفظ المستندات في حسابك وتظهر على الموقع.'),
 'freePlanDetail': ('Dokumen siap disimpan di telefon ini; salinan awan adalah sementara.', '完成的文档保存在此手机上；云端副本是临时的。', 'تُحفظ المستندات المكتملة على هذا الهاتف؛ والنسخة السحابية مؤقتة.'),
 'language': ('Bahasa', '语言', 'اللغة'),
 'phoneLanguage': ('Sama seperti telefon', '与手机相同', 'مثل الهاتف'),
 'downloadMyData': ('Muat turun data saya', '下载我的数据', 'تنزيل بياناتي'),
 'myDataSubject': ('Data Arks Scanner saya', '我的 Arks Scanner 数据', 'بياناتي في Arks Scanner'),
 'deleteMyAccount': ('Padam akaun saya', '删除我的账户', 'حذف حسابي'),
 'deleteAccountQuestion': ('Padam akaun anda?', '删除您的账户？', 'حذف حسابك؟'),
 'deleteAccountWarning': ('Akaun anda, dokumen awan dan dokumen di telefon ini akan dipadam. Tindakan ini tidak boleh dibuat asal. Taip DELETE untuk mengesahkan.',
                          '您的账户、云端文档和此手机上的文档都将被删除。此操作无法撤销。输入 DELETE 以确认。',
                          'سيتم حذف حسابك ومستنداتك السحابية والمستندات على هذا الهاتف. لا يمكن التراجع عن ذلك. اكتب DELETE للتأكيد.'),
 'deleteAccount': ('Padam akaun', '删除账户', 'حذف الحساب'),

 'privacyTitle': ('Privasi anda', '您的隐私', 'خصوصيتك'),
 'privacyBody': ('Arks Scanner memproses dokumen anda secara peribadi untuk mengimbas, mengemas dan mengeksportnya. Baca notis privasi penuh di laman web Arks Scanner.',
                 'Arks Scanner 会以保密方式处理您的文档，用于扫描、清理和导出。请在 Arks Scanner 网站上阅读完整的隐私声明。',
                 'يعالج Arks Scanner مستنداتك بشكل خاص لمسحها وتنظيفها وتصديرها. اقرأ إشعار الخصوصية الكامل على موقع Arks Scanner.'),
 'iUnderstand': ('Saya faham', '我明白了', 'فهمت'),

 'errLimit': ('Anda telah mencapai had percuma hari ini. Naik taraf ke Pro atau cuba lagi esok.', '您已达到今天的免费上限。请升级到 Pro 或明天再试。', 'لقد وصلت إلى الحد المجاني لليوم. قم بالترقية إلى Pro أو حاول غدًا.'),
 'errType': ('Jenis fail ini tidak disokong. Gunakan JPEG, PNG, HEIC atau PDF.', '不支持此文件类型。请使用 JPEG、PNG、HEIC 或 PDF。', 'نوع الملف غير مدعوم. استخدم JPEG أو PNG أو HEIC أو PDF.'),
 'errTooLarge': ('Fail ini lebih besar daripada 25 MB.', '此文件大于 25 MB。', 'هذا الملف أكبر من 25 ميغابايت.'),
 'errEmpty': ('Fail ini kosong.', '此文件为空。', 'هذا الملف فارغ.'),
 'errUpload': ('Muat naik tidak selesai. Semak sambungan anda dan cuba lagi.', '上传未完成。请检查网络后重试。', 'لم يكتمل الرفع. تحقق من اتصالك وحاول مرة أخرى.'),
 'errExportTimeout': ('PDF mengambil masa lebih lama daripada dijangka. Cuba lagi sebentar lagi.', 'PDF 生成时间比预期长，请稍后再试。', 'يستغرق ملف PDF وقتًا أطول من المتوقع. حاول بعد قليل.'),
 'errSignedOut': ('Sila log masuk semula.', '请重新登录。', 'يرجى تسجيل الدخول مرة أخرى.'),
 'errAppCheck': ('Aplikasi ini tidak dapat disahkan. Kemas kini aplikasi dan cuba lagi.', '无法验证此应用。请更新应用后重试。', 'تعذّر التحقق من هذا التطبيق. حدّث التطبيق وحاول مرة أخرى.'),
 'errServer': ('Ada masalah di pihak kami. Sila cuba lagi.', '我们这边出了问题，请重试。', 'حدث خطأ من جهتنا. حاول مرة أخرى.'),
 'errGeneric': ('Ada masalah. Sila cuba lagi.', '出了点问题，请重试。', 'حدث خطأ ما. حاول مرة أخرى.'),
 'errOverflow': ('Teks tidak muat. Pendekkan atau kecilkan teks.', '文字放不下。请缩短文字或调小字号。', 'النص لا يتسع. اختصره أو صغّر حجمه.'),
 'errUnsafeBackground': ('Kawasan ini mempunyai corak atau foto di belakang teks, jadi tidak boleh diganti dengan kemas.', '此区域文字后面有图案或照片，无法干净地替换。', 'توجد خلف النص في هذه المنطقة نقوش أو صورة، لذا لا يمكن استبداله بشكل نظيف.'),
 'errOverlap': ('Teks baharu akan menutup teks lain. Alihkan atau kecilkan.', '新文字会遮住其他文字。请移动或缩小。', 'سيغطي النص الجديد نصًا آخر. انقله أو صغّره.'),
 'errStale': ('Halaman telah berubah dan dimuat semula; sila cuba lagi.', '页面已更改并重新加载，请重试。', 'تغيّرت الصفحة وأُعيد تحميلها؛ حاول مرة أخرى.'),
 'errFontUnsupported': ('Fon ini tidak boleh menulis aksara ini. Pilih fon lain.', '此字体无法显示这些字符。请选择其他字体。', 'لا يدعم هذا الخط هذه الأحرف. اختر خطًا آخر.'),
 'errTextDisabled': ('Penyuntingan teks dimatikan buat masa ini.', '文字编辑目前已关闭。', 'تحرير النص متوقف حاليًا.'),
 'errOcrDisabled': ('Pengecaman teks dimatikan buat masa ini.', '文字识别目前已关闭。', 'التعرّف على النص متوقف حاليًا.'),
 'errPageNotReady': ('Halaman ini masih diproses. Cuba lagi sebentar lagi.', '此页面仍在处理中，请稍后再试。', 'لا تزال هذه الصفحة قيد المعالجة. حاول بعد قليل.'),
 'errRepairFailed': ('Pemadam tidak dapat membersihkan kawasan ini. Cuba kawasan yang lebih kecil.', '橡皮擦无法清理此区域。请尝试较小的区域。', 'تعذّر على الممحاة تنظيف هذه المنطقة. جرّب منطقة أصغر.'),
 'errSignatureNoInk': ('Tiada tandatangan dijumpai dalam foto ini. Gunakan foto dakwat gelap yang jelas di atas kertas cerah.', '照片中未找到签名。请使用浅色纸上深色墨迹的清晰照片。', 'لم يُعثر على توقيع في هذه الصورة. استخدم صورة واضحة لحبر داكن على ورق فاتح.'),
 'errSignatureImage': ('Imej ini tidak boleh digunakan. Gunakan PNG atau JPEG di bawah 5 MB.', '无法使用此图片。请使用小于 5 MB 的 PNG 或 JPEG。', 'لا يمكن استخدام هذه الصورة. استخدم PNG أو JPEG أقل من 5 ميغابايت.'),

 'search': ('Cari', '搜索', 'بحث'),
 'searchPlaceholder': ('Cari teks yang dikenali', '搜索已识别的文字', 'ابحث في النص المعرَّف'),
 'pagesNotRecognised': ('{count} halaman belum mempunyai teks yang dikenali.', '{count} 页尚未识别文字。', '{count} صفحة لم يُتعرَّف على نصها بعد.'),
 'noMatches': ('Tiada padanan.', '没有匹配结果。', 'لا توجد نتائج.'),
 'matchCount': ('{count} padanan', '{count} 个匹配', '{count} نتيجة'),
 'preview': ('Pratonton', '预览', 'معاينة'),
 'size': ('Saiz', '大小', 'الحجم'),
 'clear': ('Kosongkan', '清除', 'مسح'),
 'undo': ('Buat asal', '撤销', 'تراجع'),
 'redo': ('Buat semula', '重做', 'إعادة'),

 'toolEraser': ('Pemadam', '橡皮擦', 'الممحاة'),
 'undoStroke': ('Buat asal kawasan terakhir', '撤销上一个区域', 'التراجع عن آخر منطقة'),
 'eraserHint': ('Sapu di atas lubang tebuk, staples atau bintik. Hanya kawasan yang disapu akan berubah.', '涂抹打孔、订书钉或污点。只有涂抹的区域会改变。', 'لوّن فوق ثقوب التخريم أو الدبابيس أو البقع. تتغير المناطق الملوّنة فقط.'),
 'findHoles': ('Cari lubang', '查找孔洞', 'البحث عن الثقوب'),
 'noHolesFound': ('Tiada lubang tebuk dijumpai.', '未找到打孔。', 'لم يُعثر على ثقوب تخريم.'),
 'holesFound': ('{count} lubang dijumpai. Pratonton untuk membuangnya.', '找到 {count} 个孔洞。预览以去除它们。', 'عُثر على {count} ثقب. عاين لإزالتها.'),
 'brushSmall': ('Kecil', '小', 'صغير'),
 'brushMedium': ('Sederhana', '中', 'متوسط'),
 'brushLarge': ('Besar', '大', 'كبير'),
 'showBefore': ('Tunjuk sebelum', '显示修改前', 'عرض ما قبل'),

 'toolMarks': ('Tanda / Pangkah', '勾 / 叉', 'علامة صح / خطأ'),
 'toolMarksShort': ('Tanda', '勾选', 'علامة'),
 'marksHint': ('Ketik kotak pada borang untuk meletakkan tanda. Ketik tanda untuk alih, ubah saiz atau padam.', '点按表格上的方框放置标记。点按标记可移动、调整大小或删除。', 'اضغط على مربع في النموذج لوضع علامة. اضغط على علامة لنقلها أو تغيير حجمها أو حذفها.'),
 'tick': ('Tanda', '勾', 'صح'),
 'cross': ('Pangkah', '叉', 'خطأ'),

 'toolSign': ('Tambah tandatangan', '添加签名', 'إضافة توقيع'),
 'toolSignShort': ('Tandatangan', '签名', 'توقيع'),
 'signHint': ('Lukis tandatangan atau gunakan fotonya, kemudian seret ke tempatnya. Ini tandatangan visual, bukan sijil digital.', '手写签名或使用签名照片，然后拖动到位。这是可视签名，不是数字证书。', 'ارسم توقيعًا أو استخدم صورة له، ثم اسحبه إلى مكانه. هذا توقيع مرئي وليس شهادة رقمية.'),
 'drawSignature': ('Lukis', '手写', 'رسم'),
 'signaturePhoto': ('Daripada foto', '来自照片', 'من صورة'),

 'toolText': ('Teks', '文字', 'نص'),
 'replaceText': ('Ganti', '替换', 'استبدال'),
 'addText': ('Tambah teks', '添加文字', 'إضافة نص'),
 'recogniseText': ('Kenal pasti teks', '识别文字', 'التعرّف على النص'),
 'recognising': ('Mengenal pasti teks…', '正在识别文字…', 'جارٍ التعرّف على النص…'),
 'recogniseFirst': ('Kenal pasti teks pada halaman ini dahulu.', '请先识别此页面上的文字。', 'تعرّف على نص هذه الصفحة أولًا.'),
 'recogniseFailed': ('Pengecaman teks tidak selesai. Cuba lagi.', '文字识别未完成，请重试。', 'لم يكتمل التعرّف على النص. حاول مرة أخرى.'),
 'noTextFound': ('Tiada teks dijumpai pada halaman ini.', '此页面上未找到文字。', 'لم يُعثر على نص في هذه الصفحة.'),
 'replaceHint': ('Ketik perkataan untuk diganti. Cubit untuk zum.', '点按要替换的文字。双指缩放可放大。', 'اضغط على الكلمات المراد استبدالها. باعد إصبعيك للتكبير.'),
 'addTextHint': ('Ketik tempat teks baharu patut diletakkan.', '点按新文字要放置的位置。', 'اضغط حيث يجب أن يظهر النص الجديد.'),
 'editSelectedWords': ('Sunting {count} perkataan', '编辑 {count} 个词', 'تحرير {count} كلمة'),
 'originalText': ('Asal: {text}', '原文：{text}', 'الأصلي: {text}'),
 'replacementText': ('Teks pengganti', '替换文字', 'النص البديل'),
 'newText': ('Teks baharu', '新文字', 'نص جديد'),
 'moreOptions': ('Lagi pilihan', '更多选项', 'خيارات أخرى'),
 'selectOneLine': ('Hanya perkataan bersebelahan pada satu baris boleh diganti bersama, jadi pilihan baharu dimulakan.',
                   '只能一起替换同一行中相邻的词，因此已开始新的选择。',
                   'يمكن استبدال الكلمات المتجاورة في سطر واحد فقط معًا، لذلك بدأ تحديد جديد.'),
 'errSelectionInvalid': ('Pilih perkataan bersebelahan pada satu baris.', '请选择同一行中相邻的词。', 'اختر كلمات متجاورة في سطر واحد.'),
 'errSelectionUnsupported': ('Tulisan tangan tidak boleh diganti. Pilih perkataan bercetak.', '手写文字无法替换。请选择印刷文字。',
                             'لا يمكن استبدال النص المكتوب بخط اليد. اختر كلمات مطبوعة.'),
 'hideEditor': ('Sembunyikan penyunting', '隐藏编辑器', 'إخفاء المحرر'),
 'showEditor': ('Tunjukkan penyunting', '显示编辑器', 'إظهار المحرر'),
 'panelTransparency': ('Ketelusan panel', '面板透明度', 'شفافية اللوحة'),
 'moveTextHint': ('Seret kotak untuk mengalihkan teks, atau seret bucunya untuk ubah saiz. Ketik halaman juga mengalihkannya.',
                  '拖动方框可移动文字，拖动角可调整大小。点按页面也可移动。',
                  'اسحب المربع لتحريك النص، أو اسحب زاويته لتغيير الحجم. النقر على الصفحة يحرّكه أيضًا.'),
 'font': ('Fon', '字体', 'الخط'),
 'weight': ('Ketebalan', '粗细', 'السُمك'),
 'strikethrough': ('Garis tengah', '删除线', 'يتوسطه خط'),
 'letterSpacing': ('Jarak', '间距', 'التباعد'),
 'fontSans': ('Sans-serif', '无衬线', 'بدون تذييل'),
 'fontSerif': ('Serif', '衬线', 'بتذييل'),
 'fontMono': ('Monospace', '等宽', 'ثابت العرض'),
 'fontHandwriting': ('Tulisan tangan', '手写体', 'خط اليد'),
 'exactResult': ('Hasil sebenar', '实际效果', 'النتيجة الفعلية'),
}


def main():
    with open(os.path.join(ROOT, 'app_en.arb'), encoding='utf-8') as handle:
        en = json.load(handle)
    keys = [key for key in en if not key.startswith('@')]
    missing = [key for key in keys if key not in T]
    extra = [key for key in T if key not in en]
    if missing or extra:
        raise SystemExit(f'missing={missing} extra={extra}')
    for index, locale in enumerate(['ms', 'zh', 'ar']):
        out = {'@@locale': locale}
        for key in keys:
            out[key] = T[key][index]
            for placeholder in en.get(f'@{key}', {}).get('placeholders', {}):
                if '{' + placeholder + '}' not in out[key]:
                    raise SystemExit(f'{locale}.{key} lost {{{placeholder}}}')
        with open(os.path.join(ROOT, f'app_{locale}.arb'), 'w', encoding='utf-8', newline='\n') as handle:
            json.dump(out, handle, ensure_ascii=False, indent=2)
            handle.write('\n')
        print(f'wrote app_{locale}.arb ({len(keys)} keys)')


if __name__ == '__main__':
    main()
