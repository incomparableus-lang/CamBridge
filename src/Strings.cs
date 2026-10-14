using System;
using System.Collections.Generic;
using System.Globalization;

namespace CamBridge.Core
{
    /// <summary>
    /// UI strings for the supported languages. Layout is driven by the Windows Forms
    /// designer, not by a localisable resx, so this stays a plain table.
    ///
    /// Every row is { ru, be, ko, fa, zh, hi } in that exact order - the index is the
    /// language code, so rows cannot drift out of sync.
    /// </summary>
    public static class S
    {
        public static readonly string[] Codes = { "ru", "be", "ko", "fa", "zh", "hi" };

        public static readonly string[] Names =
        {
            "Русский",
            "Беларуская",
            "한국어",
            "فارسی",
            "中文",
            "हिन्दी"
        };

        private static readonly Dictionary<string, string[]> Table = Build();

        private static int _index = -1;

        /// <summary>Active language code, e.g. "fa".</summary>
        public static string Lang
        {
            get { EnsureIndex(); return Codes[_index]; }
        }

        /// <summary>Persian is the only right-to-left language we ship.</summary>
        public static bool IsRtl
        {
            get { return Lang == "fa"; }
        }

        /// <summary>
        /// Picks the language: an explicit choice wins, otherwise the current UI culture
        /// is matched by its two-letter code, otherwise Russian.
        /// </summary>
        public static void SetLanguage(string code)
        {
            int i = IndexOf(code);
            if (i >= 0)
            {
                _index = i;
                Log.Write("language set to " + Codes[i]);
            }
            else
            {
                _index = -1;
                Log.Write("language '" + code + "' not recognised, falling back to system");
            }
        }

        /// <summary>Matches a culture such as "ko-KP" against our language list.</summary>
        public static string CodeForCulture(CultureInfo culture)
        {
            if (culture == null) return "ru";
            string two = culture.TwoLetterISOLanguageName;
            if (two == null) return "ru";
            two = two.ToLowerInvariant();

            // Korean users in North Korea use the ko-KP locale; Chinese users may be on
            // any zh-* variant, all of which we serve with the simplified translation.
            if (two == "ko") return "ko";
            if (two == "zh") return "zh";
            if (two == "fa") return "fa";
            if (two == "hi") return "hi";
            if (two == "be") return "be";
            if (two == "ru") return "ru";

            // spell out the full tag in the log-worthy fallback case
            return "ru";
        }

        private static void EnsureIndex()
        {
            if (_index >= 0) return;
            int i = IndexOf(CultureInfo.CurrentUICulture == null
                ? "ru"
                : CodeForCulture(CultureInfo.CurrentUICulture));
            _index = i;
        }

        private static int IndexOf(string code)
        {
            if (string.IsNullOrEmpty(code)) return -1;
            code = code.Trim().ToLowerInvariant();
            for (int i = 0; i < Codes.Length; i++)
                if (Codes[i] == code) return i;
            return -1;
        }

        /// <summary>Translated text; falls back to Russian, then to the key itself.</summary>
        public static string T(string key)
        {
            EnsureIndex();
            string[] row;
            if (!Table.TryGetValue(key, out row)) return key;
            string value = row[_index];
            if (string.IsNullOrEmpty(value)) value = row[0];
            return value ?? key;
        }

        /// <summary>Translated text with positional format arguments.</summary>
        public static string F(string key, params object[] args)
        {
            string template = T(key);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, args);
            }
            catch
            {
                return template;
            }
        }

        private static string[] K(params string[] v)
        {
            if (v.Length != Codes.Length)
                throw new ArgumentException("each row needs " + Codes.Length + " values");
            return v;
        }

        private static Dictionary<string, string[]> Build()
        {
            Dictionary<string, string[]> d = new Dictionary<string, string[]>();

            // ---- tray menu -------------------------------------------------
            d["app"] = K("CamBridge", "CamBridge", "CamBridge", "CamBridge", "CamBridge", "CamBridge");
            d["tray_status_active"] = K(
                "Камера активна · захват {0} кадр/с",
                "Камера актыўная · захват {0} кадра/с",
                "카메라 활성 · 캡처 {0}프레임/초",
                "دوربین فعال است · دریافت {0} فریم بر ثانیه",
                "摄像头已启用 · 采集 {0} 帧/秒",
                "कैमरा सक्रिय · कैप्चर {0} फ़्रेम प्रति सेकंड");
            d["tray_status_stopped"] = K(
                "Состояние: мост не запущен",
                "Статус: мост не запушчаны",
                "상태: 브리지 실행 중이 아님",
                "وضعیت: پل اجرا نشده است",
                "状态：桥接未运行",
                "स्थिति: ब्रिज चालू नहीं है");
            d["tray_status_starting"] = K(
                "Состояние: запуск...",
                "Статус: запуск...",
                "상태: 시작 중...",
                "وضعیت: در حال راه‌اندازی...",
                "状态：正在启动…",
                "स्थिति: चालू हो रहा है…");
            d["tray_status_error"] = K(
                "Состояние: ошибка",
                "Статус: памылка",
                "상태: 오류",
                "وضعیت: خطا",
                "状态：错误",
                "स्थिति: त्रुटि");
            d["menu_settings"] = K(
                "Настройки...", "Налады...", "설정...", "تنظیمات...", "设置...", "सेटिंग्स...");
            d["menu_restart"] = K(
                "Перезапустить мост",
                "Перазапусціць мост",
                "브리지 다시 시작",
                "راه‌اندازی مجدد پل",
                "重启桥接",
                "ब्रिज पुनः प्रारंभ करें");
            d["menu_autostart"] = K(
                "Автозапуск при входе в Windows",
                "Аўтазапуск пры ўходзе ў Windows",
                "Windows 로그인 시 자동 시작",
                "اجرای خودکار هنگام ورود به ویندوز",
                "登录 Windows 时自动启动",
                "Windows में लॉगिन करने पर स्वतः चालू करें");
            d["menu_logs"] = K(
                "Открыть папку с логами",
                "Адкрыць папку з журналамі",
                "로그 폴더 열기",
                "باز کردن پوشهٔ گزارش‌ها",
                "打开日志文件夹",
                "लॉग फ़ोल्डर खोलें");
            d["menu_exit"] = K("Выход", "Выхад", "종료", "خروج", "退出", "बाहर निकलें");

            // ---- notifications --------------------------------------------
            d["balloon_not_configured"] = K(
                "CamBridge не настроен",
                "CamBridge не наладжаны",
                "CamBridge가 설정되지 않았습니다",
                "CamBridge پیکربندی نشده است",
                "CamBridge 未配置",
                "CamBridge कॉन्फ़िगर नहीं है");
            d["balloon_not_configured_hint"] = K(
                "Не выбрана камера. Откройте «Настройки» в меню значка.",
                "Камера не выбрана. Адкрыйце «Налады» ў меню значка.",
                "카메라가 선택되지 않았습니다. 트레이 아이콘의 “설정”을 여세요.",
                "دوربینی انتخاب نشده است. از منوی آیکون، «تنظیمات» را باز کنید.",
                "未选择摄像头。请从托盘图标菜单打开“设置”。",
                "कोई कैमरा नहीं चुना गया। ट्रे आइकन के मेन्यू में “सेटिंग्स” खोलें।");
            d["balloon_start_failed"] = K(
                "Не удалось запустить мост",
                "Не ўдалося запусціць мост",
                "브리지를 시작할 수 없습니다",
                "اجرای پل ممکن نشد",
                "无法启动桥接",
                "ब्रिज शुरू नहीं हो सका");
            d["balloon_error"] = K(
                "CamBridge: ошибка",
                "CamBridge: памылка",
                "CamBridge: 오류",
                "CamBridge: خطا",
                "CamBridge：错误",
                "CamBridge: त्रुटि");
            d["autostart_on"] = K(
                "Автозапуск включён",
                "Аўтазапуск уключаны",
                "자동 시작이 켜졌습니다",
                "اجرای خودکار فعال شد",
                "已启用自动启动",
                "स्वतः चालू करना चालू हो गया");
            d["autostart_off"] = K(
                "Автозапуск выключен",
                "Аўтазапуск выключаны",
                "자동 시작이 꺼졌습니다",
                "اجرای خودکار غیرفعال شد",
                "已关闭自动启动",
                "स्वतः चालू करना बंद हो गया");
            d["autostart_failed"] = K(
                "Не удалось изменить задачу автозапуска. Смотрите лог.",
                "Не ўдалося змяніць задачу аўтазапуску. Глядзіце журнал.",
                "자동 시작 작업을 변경하지 못했습니다. 로그를 확인하세요.",
                "تغییر وظیفهٔ اجرای خودکار ممکن نشد. گزارش‌ها را ببینید.",
                "无法更改自动启动任务。请查看日志。",
                "स्वतः चालू करने का कार्य बदला नहीं जा सका। लॉग देखें।");
            d["already_running"] = K(
                "CamBridge уже запущен. Значок находится в области уведомлений.",
                "CamBridge ўжо запушчаны. Значок знаходзіцца ў вобласці паведамленняў.",
                "CamBridge가 이미 실행 중입니다. 아이콘이 알림 영역에 있습니다.",
                "CamBridge از قبل اجرا شده است. نماد آن در ناحیهٔ اعلان است.",
                "CamBridge 已在运行。图标位于通知区域。",
                "CamBridge पहले से चल रहा है। आइकन सूचना क्षेत्र में है।");

            // ---- settings dialog ------------------------------------------
            d["settings_title"] = K(
                "CamBridge — настройки",
                "CamBridge — налады",
                "CamBridge — 설정",
                "CamBridge — تنظیمات",
                "CamBridge — 设置",
                "CamBridge — सेटिंग्स");
            d["lbl_device"] = K(
                "Камера (DirectShow):", "Камера (DirectShow):", "카메라 (DirectShow):",
                "دوربین (DirectShow):", "摄像头 (DirectShow)：", "कैमरा (DirectShow):");
            d["lbl_resolution"] = K(
                "Разрешение:", "Дазрэшчэнне:", "해상도:", "وضوح:", "分辨率：", "रिज़ॉल्यूशन:");
            d["lbl_fps"] = K(
                "Частота (fps):", "Частата (fps):", "프레임 속도 (fps):",
                "نرخ فریم (fps):", "帧率 (fps)：", "फ़्रेम दर (fps):");
            d["lbl_camera_name"] = K(
                "Имя виртуальной камеры:", "Назва віртуальнай камеры:", "가상 카메라 이름:",
                "نام دوربین مجازی:", "虚拟摄像头名称：", "वर्चुअल कैमरा नाम:");
            d["lbl_ffmpeg"] = K("ffmpeg.exe:", "ffmpeg.exe:", "ffmpeg.exe:", "ffmpeg.exe:", "ffmpeg.exe：", "ffmpeg.exe:");
            d["lbl_autostart"] = K(
                "Запускать мост при входе в Windows",
                "Запускаць мост пры ўходзе ў Windows",
                "Windows 로그인 시 브리지 시작",
                "اجرای پل هنگام ورود به ویندوز",
                "登录 Windows 时启动桥接",
                "Windows में लॉगिन करने पर ब्रिज चालू करें");
            d["lbl_language"] = K(
                "Язык:", "Мова:", "언어:", "زبان:", "语言：", "भाषा:");
            d["btn_refresh"] = K(
                "Обновить", "Абнавіць", "새로 고침", "تازه‌سازی", "刷新", "ताज़ा करें");
            d["btn_save"] = K(
                "Сохранить", "Захаваць", "저장", "ذخیره", "保存", "सहेजें");
            d["btn_cancel"] = K(
                "Отмена", "Скасаваць", "취소", "لغو", "取消", "रद्द करें");
            d["btn_browse"] = K(
                "Обзор...", "Прагляд...", "찾아보기...", "مرور...", "浏览...", "ब्राउज़ करें...");
            d["btn_test"] = K(
                "Проверить", "Праверыць", "테스트", "آزمایش", "测试", "जाँचें");
            d["no_cameras"] = K(
                "(камеры DirectShow не найдены — проверьте подключение)",
                "(камеры DirectShow не знойдзена — праверце падключэнне)",
                "(DirectShow 카메라를 찾을 수 없습니다. 연결을 확인하세요)",
                "(دوربین DirectShow یافت نشد — اتصال را بررسی کنید)",
                "(未找到 DirectShow 摄像头 — 请检查连接)",
                "(DirectShow कैमरा नहीं मिला — कनेक्शन जाँचें)");
            d["default_camera_name"] = K(
                "Камера", "Камера", "카메라", "دوربین", "摄像头", "कैमरा");
            d["testing"] = K(
                "проверка...", "праверка...", "테스트 중...", "در حال آزمایش...", "测试中…", "जाँच हो रही है…");
            d["test_ok"] = K(
                "виртуальная камера создана",
                "віртуальная камера створана",
                "가상 카메라가 생성되었습니다",
                "دوربین مجازی ساخته شد",
                "虚拟摄像头已创建",
                "वर्चुअल कैमरा बन गया");

            // ---- installer wizard -----------------------------------------
            d["setup_title"] = K(
                "Установка CamBridge", "Устаноўка CamBridge", "CamBridge 설치",
                "نصب CamBridge", "安装 CamBridge", "CamBridge संस्थापना");
            d["setup_page1_title"] = K(
                "CamBridge — установка виртуальной камеры",
                "CamBridge — устаноўка віртуальнай камеры",
                "CamBridge — 가상 카메라 설치",
                "CamBridge — نصب دوربین مجازی",
                "CamBridge — 安装虚拟摄像头",
                "CamBridge — वर्चुअल कैमरा संस्थापना");
            d["setup_page1_body"] = K(
                "CamBridge делает обычную DirectShow-камеру видимой в приложениях, которые "
                + "работают через Media Foundation: «Камера», Teams, Zoom, браузеры. "
                + "Системная камера не изменяется, рядом появляется виртуальная.",
                "CamBridge робіць звычайную DirectShow-камеру бачнай у праграмах, якія "
                + "працуюць праз Media Foundation: «Камера», Teams, Zoom, браўзеры. "
                + "Сістэмная камера не змяняецца, побач з’яўляецца віртуальная.",
                "CamBridge는 Media Foundation를 사용하는 앱(카메라, Teams, Zoom, 브라우저)에서 "
                + "일반 DirectShow 카메라가 보이도록 합니다. 시스템 카메라는 변경되지 않고 "
                + "가상 카메라가 함께 나타납니다.",
                "CamBridge دوربین معمولی DirectShow را در برنامه‌هایی که از Media Foundation "
                + "استفاده می‌کنند — دوربین، Teams، Zoom و مرورگرها — قابل مشاهده می‌کند. "
                + "دوربین سیستم تغییری نمی‌کند و یک دوربین مجازی در کنار آن ظاهر می‌شود.",
                "CamBridge 让普通 DirectShow 摄像头在使用 Media Foundation 的应用中可见："
                + "“相机”、Teams、Zoom、浏览器等。系统摄像头不会被修改，只是在旁边多出一个虚拟摄像头。",
                "CamBridge सामान्य DirectShow कैमरा को उन ऐप्स में दिखाता है जो Media "
                + "Foundation का उपयोग करते हैं: कैमरा, Teams, Zoom, ब्राउज़र। सिस्टम कैमरा "
                + "बदला नहीं जाता, बस साथ में एक वर्चुअल कैमरा दिखता है।");
            d["setup_page2_title"] = K(
                "Выбор камеры", "Выбар камеры", "카메라 선택", "انتخاب دوربین", "选择摄像头", "कैमरा चुनें");
            d["setup_page2_body"] = K(
                "Выберите камеру, которую нужно транслировать, и разрешение. Это можно изменить "
                + "позже через значок CamBridge в трее.",
                "Абярыце камеру, якую трэба трансляваць, і дазрэшчэнне. Гэта можна змяніць "
                + "пазней праз значок CamBridge у трэі.",
                "전송할 카메라와 해상도를 선택하세요. 나중에 트레이의 CamBridge 아이콘에서 "
                + "변경할 수 있습니다.",
                "دوربینی را که باید پخش شود و وضوح آن را انتخاب کنید. بعداً می‌توانید آن را "
                + "از طریق نماد CamBridge در نوار وظیفه تغییر دهید.",
                "选择要传输的摄像头和分辨率。之后可以通过托盘中的 CamBridge 图标更改。",
                "वह कैमरा और रिज़ॉल्यूशन चुनें जिसे स्ट्रीम करना है। बाद में ट्रे के "
                + "CamBridge आइकन से बदला जा सकता है।");
            d["setup_page3_title"] = K(
                "Готово", "Гатова", "완료", "پایان", "完成", "हो गया");
            d["setup_page3_body"] = K(
                "CamBridge запущен и появился в списке камер как «виртуальная камера». "
                + "Выберите её в приложении, где нужна камера.",
                "CamBridge запушчаны і з’явіўся ў спісе камер як «віртуальная камера». "
                + "Абярыце яго ў праграме, дзе патрэбна камера.",
                "CamBridge가 실행되었고 카메라 목록에 “가상 카메라”로 나타났습니다. "
                + "카메라가 필요한 앱에서 선택하세요.",
                "CamBridge اجرا شد و به‌عنوان «دوربین مجازی» در فهرست دوربین‌ها ظاهر شد. "
                + "آن را در برنامه‌ای که به دوربین نیاز دارد انتخاب کنید.",
                "CamBridge 已启动，并作为“虚拟摄像头”出现在摄像头列表中。请在需要摄像头的应用中选用它。",
                "CamBridge चल गया है और कैमरा सूची में “वर्चुअल कैमरा” के रूप में दिख रहा है। "
                + "जिस ऐप में कैमरा चाहिए, वहाँ इसे चुनें।");
            d["setup_step"] = K(
                "Шаг {0} из 3", "Крок {0} з 3", "{0}단계 / 3", "مرحلهٔ {0} از ۳", "第 {0} 步，共 3 步", "चरण {0} / 3");
            d["btn_next"] = K("Далее", "Далей", "다음", "بعدی", "下一步", "आगे");
            d["btn_back"] = K("Назад", "Назад", "뒤로", "بازگشت", "上一步", "पीछे");
            d["btn_finish"] = K("Готово", "Гатова", "완료", "پایان", "完成", "हो गया");
            d["btn_quit"] = K("Выход", "Выхад", "종료", "خروج", "退出", "बाहर निकलें");
            d["setup_preparing"] = K(
                "Подготовка...", "Падрыхтоўка...", "준비 중...", "در حال آماده‌سازی...", "准备中…", "तैयारी हो रही है…");
            d["setup_installing"] = K(
                "Установка компонентов...", "Устаноўка кампанентаў...",
                "구성 요소 설치 중...", "نصب مؤلفه‌ها...", "正在安装组件…", "घटक स्थापित हो रहे हैं…");
            d["setup_error"] = K(
                "Ошибка установки", "Памылка ўстаноўкі", "설치 오류", "خطای نصب", "安装错误", "संस्थापना त्रुटि");

            // ---- install steps -------------------------------------------
            d["inst_copy"] = K(
                "Копирование компонентов...", "Капаванне кампанентаў...",
                "구성 요소 복사 중...", "در حال کپی مؤلفه‌ها...", "正在复制组件…", "घटक कॉपी हो रहे हैं…");
            d["inst_ffmpeg"] = K(
                "Копирование ffmpeg...", "Капаванне ffmpeg...",
                "ffmpeg 복사 중...", "در حال کپی ffmpeg...", "正在复制 ffmpeg…", "ffmpeg कॉपी हो रहा है…");
            d["inst_vc_check"] = K(
                "Проверка Visual C++ Runtime...", "Праверка Visual C++ Runtime...",
                "Visual C++ Runtime 확인 중...", "بررسی Visual C++ Runtime...",
                "正在检查 Visual C++ Runtime…", "Visual C++ Runtime जाँच हो रहा है…");
            d["inst_vc_download"] = K(
                "Скачивание Visual C++ Runtime...", "Спампоўванне Visual C++ Runtime...",
                "Visual C++ Runtime 다운로드 중...", "در حال دانلود Visual C++ Runtime...",
                "正在下载 Visual C++ Runtime…", "Visual C++ Runtime डाउनलोड हो रहा है…");
            d["inst_vc_install"] = K(
                "Установка Visual C++ Runtime...", "Устаноўка Visual C++ Runtime...",
                "Visual C++ Runtime 설치 중...", "نصب Visual C++ Runtime...",
                "正在安装 Visual C++ Runtime…", "Visual C++ Runtime इंस्टॉल हो रहा है…");
            d["inst_mf"] = K(
                "Установка виртуальной камеры (Media Foundation)...",
                "Устаноўка віртуальнай камеры (Media Foundation)...",
                "가상 카메라(Media Foundation) 설치 중...",
                "نصب دوربین مجازی (Media Foundation)...",
                "正在安装虚拟摄像头（Media Foundation）…",
                "वर्चुअल कैमरा (Media Foundation) स्थापित हो रहा है…");
            d["inst_done"] = K(
                "Установлено.", "Устаноўлена.", "설치 완료.", "نصب شد.", "已安装。", "स्थापित हो गया।");

            // ---- errors ---------------------------------------------------
            d["err_setup_failed_title"] = K(
                "Установка не завершена", "Устаноўка не завершана", "설치가 완료되지 않았습니다",
                "نصب کامل نشد", "安装未完成", "संस्थापना पूर्ण नहीं हुई");
            d["err_msi"] = K(
                "Не удалось установить компонент виртуальной камеры (код msiexec {0}).",
                "Не ўдалося ўсталяваць кампанент віртуальнай камеры (код msiexec {0}).",
                "가상 카메라 구성 요소를 설치하지 못했습니다(msiexec 코드 {0}).",
                "نصب مؤلفهٔ دوربین مجازی ممکن نشد (کد msiexec {0}).",
                "无法安装虚拟摄像头组件（msiexec 代码 {0}）。",
                "वर्चुअल कैमरा घटक स्थापित नहीं हो सका (msiexec कोड {0})।");
            d["err_msi_hint"] = K(
                "Закройте программы, использующие камеру, и повторите установку.",
                "Закрыйце праграмы, якія выкарыстоўваюць камеру, і паўтарыце ўстаноўку.",
                "카메라를 사용하는 프로그램을 닫고 다시 설치하세요.",
                "برنامه‌هایی را که از دوربین استفاده می‌کنند ببندید و نصب را تکرار کنید.",
                "请关闭正在使用摄像头的程序，然后重新安装。",
                "कैमरा उपयोग करने वाले प्रोग्राम बंद करें और फिर से संस्थापना करें।");
            d["err_vc"] = K(
                "Не удалось установить Visual C++ Runtime (код {0}).",
                "Не ўдалося ўсталяваць Visual C++ Runtime (код {0}).",
                "Visual C++ Runtime을 설치하지 못했습니다(코드 {0}).",
                "نصب Visual C++ Runtime ممکن نشد (کد {0}).",
                "无法安装 Visual C++ Runtime（代码 {0}）。",
                "Visual C++ Runtime स्थापित नहीं हो सका (कोड {0})।");
            d["err_vc_download"] = K(
                "Не удалось скачать Visual C++ Runtime. Проверьте интернет.",
                "Не ўдалося спампаваць Visual C++ Runtime. Праверце інтэрнэт.",
                "Visual C++ Runtime을 다운로드하지 못했습니다. 인터넷을 확인하세요.",
                "دانلود Visual C++ Runtime ممکن نشد. اتصال اینترنت را بررسی کنید.",
                "无法下载 Visual C++ Runtime。请检查网络。",
                "Visual C++ Runtime डाउनलोड नहीं हो सका। इंटरनेट जाँचें।");
            d["err_extract"] = K(
                "Не удалось распаковать компоненты установщика:",
                "Не ўдалося распакаваць кампаненты ўстаноўшчыка:",
                "설치 관리자 구성 요소를 풀지 못했습니다:",
                "باز کردن مؤلفه‌های نصب‌کننده ممکن نشد:",
                "无法解压安装程序组件：",
                "इंस्टॉलर घटक निकाले नहीं जा सके:");
            d["err_crash"] = K(
                "Установщик завершился с ошибкой:",
                "Устаноўшчык завершыўся з памылкай:",
                "설치 관리자가 오류로 종료되었습니다:",
                "نصب‌کننده با خطا پایان یافت:",
                "安装程序因错误而终止：",
                "इंस्टॉलर त्रुटि पर समाप्त हुआ:");
            d["err_details"] = K(
                "Подробности:", "Падрабязнасці:", "자세한 내용:", "جزئیات:", "详细信息：", "विवरण:");

            // ---- uninstaller / ARP ---------------------------------------
            d["uninst_title"] = K(
                "Удаление CamBridge", "Выдаленне CamBridge", "CamBridge 제거",
                "حذف CamBridge", "卸载 CamBridge", "CamBridge हटाएँ");
            d["uninst_question"] = K(
                "Удалить CamBridge?", "Выдаліць CamBridge?", "CamBridge를 제거할까요?",
                "CamBridge حذف شود؟", "要卸载 CamBridge 吗？", "CamBridge हटाएँ?");
            d["uninst_body"] = K(
                "Будет удалён мост и виртуальная камера. Сама камера и её драйвер останутся нетронутыми.",
                "Будзе выдалены мост і віртуальная камера. Сама камера і яе драйвер застануцца некранутымі.",
                "브리지와 가상 카메라가 제거됩니다. 실제 카메라와 드라이버는 그대로 유지됩니다.",
                "پل و دوربین مجازی حذف می‌شوند. دوربین اصلی و درایور آن دست‌نخورده باقی می‌مانند.",
                "将移除桥接和虚拟摄像头。摄像头本身及其驱动不会受到影响。",
                "ब्रिज और वर्चुअल कैमरा हटा दिए जाएँगे। कैमरा और उसका ड्राइवर अपरिवर्तित रहेगा।");
            d["uninst_error"] = K(
                "Ошибка при удалении:", "Памылка пры выдаленні:", "제거 중 오류:", "خطا هنگام حذف:", "卸载时出错：", "हटाने में त्रुटि:");
            d["arp_name"] = K(
                "CamBridge (виртуальная камера)", "CamBridge (віртуальная камера)",
                "CamBridge (가상 카메라)", "CamBridge (دوربین مجازی)",
                "CamBridge（虚拟摄像头）", "CamBridge (वर्चुअल कैमरा)");

            // ---- language names, shown in their own script ---------------
            d["lang_name"] = K(Names[0], Names[1], Names[2], Names[3], Names[4], Names[5]);

            return d;
        }
    }
}
