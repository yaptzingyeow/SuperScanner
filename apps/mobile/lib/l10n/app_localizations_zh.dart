// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Chinese (`zh`).
class AppLocalizationsZh extends AppLocalizations {
  AppLocalizationsZh([String locale = 'zh']) : super(locale);

  @override
  String get appName => 'Arks Scanner';

  @override
  String get tagline => '扫描、清理并签署文档。默认保密。';

  @override
  String get continueWithGoogle => '使用 Google 继续';

  @override
  String get or => '或';

  @override
  String get email => '电子邮件';

  @override
  String get password => '密码';

  @override
  String get createAccount => '创建账户';

  @override
  String get signInWithEmail => '使用电子邮件登录';

  @override
  String get haveAccount => '我已有账户';

  @override
  String get createAnAccount => '创建新账户';

  @override
  String get forgotPassword => '忘记密码？';

  @override
  String get enterEmailFirst => '请先输入您的电子邮件。';

  @override
  String get resetSent => '请查看电子邮件中的重置链接。';

  @override
  String get authIncorrect => '电子邮件或密码不正确。';

  @override
  String get authEmailInUse => '此电子邮件已被使用，请直接登录。';

  @override
  String get authWeakPassword => '请使用至少 6 个字符的密码。';

  @override
  String get authInvalidEmail => '请输入有效的电子邮件地址。';

  @override
  String get authNoConnection => '无网络连接。请检查网络后重试。';

  @override
  String get authCancelled => '已取消登录。';

  @override
  String get authFailed => '登录未完成，请重试。';

  @override
  String get myDocuments => '我的文档';

  @override
  String get settingsPrivacy => '设置与隐私';

  @override
  String get signOut => '退出登录';

  @override
  String get signOutQuestion => '退出登录？';

  @override
  String signOutWarning(int count) {
    return '仅保存在此手机上的 $count 个文档将被删除。请先分享或保存需要保留的文档。';
  }

  @override
  String get cancel => '取消';

  @override
  String get save => '保存';

  @override
  String get delete => '删除';

  @override
  String get rename => '重命名';

  @override
  String get apply => '应用';

  @override
  String get done => '完成';

  @override
  String get scan => '扫描';

  @override
  String get importPhotosPdf => '导入照片或 PDF';

  @override
  String get uploadingPages => '正在上传页面…';

  @override
  String get onThisPhone => '在此手机上';

  @override
  String get inYourAccount => '在您的账户中';

  @override
  String get inProgress => '编辑中';

  @override
  String get noDocuments => '还没有文档。点按\"扫描\"开始。';

  @override
  String pagesAndDate(int count, String date) {
    return '$count 页 · $date';
  }

  @override
  String get document => '文档';

  @override
  String get processingPages => '正在处理页面……可以先编辑已完成的页面。';

  @override
  String pageNumber(int number) {
    return '第 $number 页';
  }

  @override
  String get pageFailed => '无法处理此页面';

  @override
  String get ready => '就绪';

  @override
  String get processing => '处理中…';

  @override
  String get removePage => '删除页面';

  @override
  String get add => '添加';

  @override
  String get share => '分享';

  @override
  String get print => '打印';

  @override
  String get saveCopy => '保存副本';

  @override
  String get saveToPhone => '保存到手机';

  @override
  String get savedToPhone => '已保存到此手机。';

  @override
  String get savedToPhoneFree => '已保存到此手机。云端副本将被删除。';

  @override
  String get page => '页面';

  @override
  String get rotateLeft => '向左旋转';

  @override
  String get rotateRight => '向右旋转';

  @override
  String get corners => '角点';

  @override
  String get autoDetect => '自动检测';

  @override
  String get adjustCorners => '调整角点';

  @override
  String get lookMagic => '魔法扫描';

  @override
  String get lookOriginal => '原图';

  @override
  String get lookDocument => '文档';

  @override
  String get lookBright => '明亮';

  @override
  String get lookRemoveShadows => '去阴影';

  @override
  String get lookGrayscale => '灰度';

  @override
  String get lookBlackAndWhite => '黑白';

  @override
  String get noLongerOnPhone => '此文档已不在此手机上。';

  @override
  String get deleteFromPhone => '从此手机中删除？';

  @override
  String get cannotUndo => '此操作无法撤销。';

  @override
  String swipeForMore(int count) {
    return '$count 页 · 滑动查看更多';
  }

  @override
  String get editAgain => '再次编辑';

  @override
  String get signedIn => '已登录';

  @override
  String get proPlan => 'Pro 方案';

  @override
  String get freePlan => '免费方案';

  @override
  String get proPlanDetail => '文档保存在您的账户中，并显示在网站上。';

  @override
  String get freePlanDetail => '完成的文档保存在此手机上；云端副本是临时的。';

  @override
  String get language => '语言';

  @override
  String get phoneLanguage => '与手机相同';

  @override
  String get downloadMyData => '下载我的数据';

  @override
  String get myDataSubject => '我的 Arks Scanner 数据';

  @override
  String get deleteMyAccount => '删除我的账户';

  @override
  String get deleteAccountQuestion => '删除您的账户？';

  @override
  String get deleteAccountWarning =>
      '您的账户、云端文档和此手机上的文档都将被删除。此操作无法撤销。输入 DELETE 以确认。';

  @override
  String get deleteAccount => '删除账户';

  @override
  String get privacyTitle => '您的隐私';

  @override
  String get privacyBody =>
      'Arks Scanner 会以保密方式处理您的文档，用于扫描、清理和导出。请在 Arks Scanner 网站上阅读完整的隐私声明。';

  @override
  String get iUnderstand => '我明白了';

  @override
  String get errLimit => '您已达到今天的免费上限。请升级到 Pro 或明天再试。';

  @override
  String get errType => '不支持此文件类型。请使用 JPEG、PNG、HEIC 或 PDF。';

  @override
  String get errTooLarge => '此文件大于 25 MB。';

  @override
  String get errEmpty => '此文件为空。';

  @override
  String get errUpload => '上传未完成。请检查网络后重试。';

  @override
  String get errExportTimeout => 'PDF 生成时间比预期长，请稍后再试。';

  @override
  String get errSignedOut => '请重新登录。';

  @override
  String get errAppCheck => '无法验证此应用。请更新应用后重试。';

  @override
  String get errServer => '我们这边出了问题，请重试。';

  @override
  String get errGeneric => '出了点问题，请重试。';

  @override
  String get exportOptions => '导出选项';

  @override
  String get addWatermark => '添加水印';

  @override
  String get watermarkSubtitle => '在每一页加盖，例如“仅限政府使用”。';

  @override
  String get wmText => '文字';

  @override
  String get wmQuickText => '快捷文字';

  @override
  String get wmOnce => '单个';

  @override
  String get wmRepeated => '重复';

  @override
  String get wmPosition => '位置';

  @override
  String get wmTop => '顶部';

  @override
  String get wmCenter => '居中';

  @override
  String get wmBottom => '底部';

  @override
  String get wmFont => '字体';

  @override
  String get wmBold => '粗体';

  @override
  String get wmColour => '颜色';

  @override
  String get wmTransparency => '透明度';

  @override
  String get wmSize => '大小';

  @override
  String get wmAngle => '角度';

  @override
  String get wmSpacing => '间距';

  @override
  String get wmReset => '恢复默认';

  @override
  String get wmPreviewNote => '第 1 页预览。PDF 将使用所选字体。';

  @override
  String get wmPresetGovernment => '仅供政府使用';

  @override
  String get wmPresetIc => '仅供身份证核验使用';

  @override
  String get wmPresetConfidential => '机密';

  @override
  String get wmPresetCopy => '副本';

  @override
  String get wmPresetDraft => '草稿';

  @override
  String get wmPresetSample => '样本';

  @override
  String get errWatermark => '无法使用此水印。请使用 1–120 个普通文字字符。';

  @override
  String get errNoConnection => '无法连接服务器。请检查网络连接后重试。';

  @override
  String get errForbidden => '服务器拒绝了此请求。请退出后重新登录；如仍出现，此应用版本可能未通过验证。';

  @override
  String get errNotFound => '此文档或页面已不存在。请返回并刷新。';

  @override
  String get errRejected => '服务器无法接受此更改。请尝试其他选择或设置。';

  @override
  String get errTooMany => '请求过多。请稍等后重试。';

  @override
  String errorCode(String detail) {
    return '错误代码：$detail';
  }

  @override
  String get errOverflow => '文字放不下。请缩短文字或调小字号。';

  @override
  String get errUnsafeBackground => '此区域文字后面有图案或照片，无法干净地替换。';

  @override
  String get errOverlap => '新文字会遮住其他文字。请移动或缩小。';

  @override
  String get errStale => '页面已更改并重新加载，请重试。';

  @override
  String get errFontUnsupported => '此字体无法显示这些字符。请选择其他字体。';

  @override
  String get errTextDisabled => '文字编辑目前已关闭。';

  @override
  String get errOcrDisabled => '文字识别目前已关闭。';

  @override
  String get errPageNotReady => '此页面仍在处理中，请稍后再试。';

  @override
  String get errRepairFailed => '橡皮擦无法清理此区域。请尝试较小的区域。';

  @override
  String get errSignatureNoInk => '照片中未找到签名。请使用浅色纸上深色墨迹的清晰照片。';

  @override
  String get errSignatureImage => '无法使用此图片。请使用小于 5 MB 的 PNG 或 JPEG。';

  @override
  String get search => '搜索';

  @override
  String get searchPlaceholder => '搜索已识别的文字';

  @override
  String pagesNotRecognised(int count) {
    return '$count 页尚未识别文字。';
  }

  @override
  String get noMatches => '没有匹配结果。';

  @override
  String matchCount(int count) {
    return '$count 个匹配';
  }

  @override
  String get preview => '预览';

  @override
  String get size => '大小';

  @override
  String get clear => '清除';

  @override
  String get undo => '撤销';

  @override
  String get redo => '重做';

  @override
  String get toolEraser => '橡皮擦';

  @override
  String get undoStroke => '撤销上一个区域';

  @override
  String get eraserHint => '涂抹打孔、订书钉或污点。只有涂抹的区域会改变。';

  @override
  String get findHoles => '查找孔洞';

  @override
  String get noHolesFound => '未找到打孔。';

  @override
  String holesFound(int count) {
    return '找到 $count 个孔洞。预览以去除它们。';
  }

  @override
  String get brushSmall => '小';

  @override
  String get brushMedium => '中';

  @override
  String get brushLarge => '大';

  @override
  String get showBefore => '显示修改前';

  @override
  String get toolMarks => '勾 / 叉';

  @override
  String get toolMarksShort => '勾选';

  @override
  String get marksHint => '点按表格上的方框放置标记。点按标记可移动、调整大小或删除。';

  @override
  String get tick => '勾';

  @override
  String get cross => '叉';

  @override
  String get toolSign => '添加签名';

  @override
  String get toolSignShort => '签名';

  @override
  String get signHint => '手写签名或使用签名照片，然后拖动到位。这是可视签名，不是数字证书。';

  @override
  String get drawSignature => '手写';

  @override
  String get signaturePhoto => '来自照片';

  @override
  String get toolText => '文字';

  @override
  String get replaceText => '替换';

  @override
  String get addText => '添加文字';

  @override
  String get recogniseText => '识别文字';

  @override
  String get recognising => '正在识别文字…';

  @override
  String get recogniseFirst => '请先识别此页面上的文字。';

  @override
  String get recogniseFailed => '文字识别未完成，请重试。';

  @override
  String get noTextFound => '此页面上未找到文字。';

  @override
  String get replaceHint => '点按要替换的文字。双指缩放可放大。';

  @override
  String get addTextHint => '点按新文字要放置的位置。';

  @override
  String editSelectedWords(int count) {
    return '编辑 $count 个词';
  }

  @override
  String originalText(String text) {
    return '原文：$text';
  }

  @override
  String get replacementText => '替换文字';

  @override
  String get newText => '新文字';

  @override
  String get moreOptions => '更多选项';

  @override
  String get selectOneLine => '只能一起替换同一行中相邻的词，因此已开始新的选择。';

  @override
  String get errSelectionInvalid => '请选择同一行中相邻的词。';

  @override
  String get errSelectionUnsupported => '手写文字无法替换。请选择印刷文字。';

  @override
  String get hideEditor => '隐藏编辑器';

  @override
  String get showEditor => '显示编辑器';

  @override
  String get panelTransparency => '面板透明度';

  @override
  String get moveTextHint => '拖动方框可移动文字，拖动角可调整大小。点按页面也可移动。';

  @override
  String get font => '字体';

  @override
  String get weight => '粗细';

  @override
  String get strikethrough => '删除线';

  @override
  String get letterSpacing => '间距';

  @override
  String get fontSans => '无衬线';

  @override
  String get fontSerif => '衬线';

  @override
  String get fontMono => '等宽';

  @override
  String get fontHandwriting => '手写体';

  @override
  String get exactResult => '实际效果';
}
