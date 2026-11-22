using System.Collections.Generic;

namespace AGCleaner
{
    /// <summary>1.7.0 新增词条的多语言补充（中文原文为键）。</summary>
    public static class LangExtra
    {
        private static readonly string[][] Rows = new string[][]
        {
            new string[] { "2345 浏览器", "2345 Browser", "2345 Browser", "Navigateur 2345", "Браузер 2345", "Navegador 2345" },
            new string[] {
                "当前未以管理员身份运行，系统级缓存无法扫描；右键“以管理员身份运行”后可见",
                "Not running as administrator, so system-level caches are skipped. Start the app as administrator to include them.",
                "Nicht als Administrator gestartet, Systemcaches werden übersprungen. Starten Sie das Programm als Administrator.",
                "Lancement sans droits d’administrateur : les caches système sont ignorés. Relancez le programme en tant qu’administrateur.",
                "Запуск без прав администратора: системные кэши пропущены. Запустите программу от имени администратора.",
                "Sin permisos de administrador: los cachés del sistema se omiten. Ejecuta el programa como administrador." },
            new string[] {
                "回收站中已删除的文件（勾选后彻底清空，清空不可撤销）",
                "Files deleted into the Recycle Bin (checked items are erased for good; this cannot be undone)",
                "Gelöschte Dateien im Papierkorb (markierte werden endgültig gelöscht, keine Rückgängig-Funktion)",
                "Fichiers supprimés dans la Corbeille (les éléments cochés sont effacés définitivement)",
                "Файлы, удалённые в корзину (отмеченные стираются безвозвратно)",
                "Archivos eliminados en la Papelera (los marcados se borran de forma definitiva)" },
            new string[] { "空键，无任何值与子键", "Empty key: no values or subkeys", "Leerer Schlüssel: keine Werte oder Unterschlüssel", "Clé vide : aucune valeur ni sous-clé", "Пустой раздел: нет значений и подразделов", "Clave vacía: sin valores ni subclaves" },
            new string[] { "空目录：", "Empty folders: ", "Leere Ordner: ", "Dossiers vides : ", "Пустые папки: ", "Carpetas vacías: " },
            new string[] { "空文件", "Empty files", "Leere Dateien", "Fichiers vides", "Пустые файлы", "Archivos vacíos" },
            new string[] { "空文件：", "Empty files: ", "Leere Dateien: ", "Fichiers vides : ", "Пустые файлы: ", "Archivos vacíos: " },
            new string[] { "空注册表键", "Empty registry keys", "Leere Registrierungsschlüssel", "Clés de registre vides", "Пустые разделы реестра", "Claves de registro vacías" },
            new string[] { "空注册表键：", "Empty registry keys: ", "Leere Registrierungsschlüssel: ", "Clés de registre vides : ", "Пустые разделы реестра: ", "Claves de registro vacías: " },
            new string[] { "猎豹浏览器", "LieBao Browser", "LieBao Browser", "Navigateur LieBao", "Браузер LieBao", "Navegador LieBao" },
            new string[] { "目录仍存在：", "Folder still exists: ", "Ordner besteht noch: ", "Le dossier existe encore : ", "Папка всё ещё существует: ", "La carpeta aún existe: " },
            new string[] {
                @"驱动解压临时目录 C:\Drivers",
                @"Driver extraction temp folder C:\Drivers",
                @"Temporärer Treiberordner C:\Drivers",
                @"Dossier temporaire des pilotes C:\Drivers",
                @"Временная папка драйверов C:\Drivers",
                @"Carpeta temporal de controladores C:\Drivers" },
            new string[] { "扫描空目录...", "Scanning empty folders...", "Leere Ordner werden gesucht...", "Recherche des dossiers vides...", "Поиск пустых папок...", "Buscando carpetas vacías..." },
            new string[] { "扫描空目录与空文件...", "Scanning empty folders and files...", "Leere Ordner und Dateien werden gesucht...", "Recherche des dossiers et fichiers vides...", "Поиск пустых папок и файлов...", "Buscando carpetas y archivos vacíos..." },
            new string[] { "扫描空文件...", "Scanning empty files...", "Leere Dateien werden gesucht...", "Recherche des fichiers vides...", "Поиск пустых файлов...", "Buscando archivos vacíos..." },
            new string[] { "扫描空注册表键...", "Scanning empty registry keys...", "Leere Registrierungsschlüssel werden gesucht...", "Recherche des clés de registre vides...", "Поиск пустых разделов реестра...", "Buscando claves de registro vacías..." },
            new string[] { "删除空目录 ", "Remove empty folder ", "Leeren Ordner entfernen ", "Supprimer le dossier vide ", "Удалить пустую папку ", "Eliminar carpeta vacía " },
            new string[] { "删除空文件 ", "Remove empty file ", "Leere Datei entfernen ", "Supprimer le fichier vide ", "Удалить пустой файл ", "Eliminar archivo vacío " },
            new string[] { "搜狗浏览器", "Sogou Browser", "Sogou Browser", "Navigateur Sogou", "Браузер Sogou", "Navegador Sogou" },
            new string[] { "跳过（受保护）：", "Skipped (protected): ", "Übersprungen (geschützt): ", "Ignoré (protégé) : ", "Пропущено (защищено): ", "Omitido (protegido): " },
            new string[] { "文件仍存在：", "File still exists: ", "Datei besteht noch: ", "Le fichier existe encore : ", "Файл всё ещё существует: ", "El archivo aún existe: " },
            new string[] { "已删除空目录：", "Empty folder removed: ", "Leerer Ordner entfernt: ", "Dossier vide supprimé : ", "Пустая папка удалена: ", "Carpeta vacía eliminada: " },
            new string[] { "已删除空文件：", "Empty file removed: ", "Leere Datei entfernt: ", "Fichier vide supprimé : ", "Пустой файл удалён: ", "Archivo vacío eliminado: " },
            new string[] {
                "桌面、文档、下载、图片、音乐、视频里 7 天前创建、大小为 0 的文件",
                "Zero-byte files created more than 7 days ago in Desktop, Documents, Downloads, Pictures, Music and Videos",
                "0-Byte-Dateien, älter als 7 Tage, in Desktop, Dokumente, Downloads, Bilder, Musik und Videos",
                "Fichiers de 0 octet créés il y a plus de 7 jours dans Bureau, Documents, Téléchargements, Images, Musique et Vidéos",
                "Файлы нулевого размера старше 7 дней в папках Рабочий стол, Документы, Загрузки, Изображения, Музыка и Видео",
                "Archivos de 0 bytes creados hace más de 7 días en Escritorio, Documentos, Descargas, Imágenes, Música y Vídeos" },
            new string[] {
                "桌面、文档、下载、图片、音乐、视频里不含任何文件的目录（删除不影响任何数据）",
                "Folders holding no files at all inside Desktop, Documents, Downloads, Pictures, Music and Videos (removing them loses nothing)",
                "Ordner ohne jede Datei in Desktop, Dokumente, Downloads, Bilder, Musik und Videos (es geht nichts verloren)",
                "Dossiers ne contenant aucun fichier dans Bureau, Documents, Téléchargements, Images, Musique et Vidéos (aucune perte)",
                "Папки без единого файла в Рабочем столе, Документах, Загрузках, Изображениях, Музыке и Видео (данные не теряются)",
                "Carpetas sin ningún archivo en Escritorio, Documentos, Descargas, Imágenes, Música y Vídeos (no se pierde nada)" },
            new string[] { "AMD 驱动安装包", "AMD driver packages", "AMD-Treiberpakete", "Paquets de pilotes AMD", "Пакеты драйверов AMD", "Paquetes de controladores AMD" },
            new string[] {
                @"HKCU\Software 下既没有值也没有子键的空壳键（直接删除，软件会按需重建）",
                @"Shell keys under HKCU\Software with no values and no subkeys (deleted directly; apps recreate them when needed)",
                @"Leere Schlüssel unter HKCU\Software ohne Werte und Unterschlüssel (vor dem Löschen gesichert)",
                @"Clés vides sous HKCU\Software sans valeur ni sous-clé (sauvegardées avant suppression)",
                @"Пустые разделы в HKCU\Software без значений и подразделов (перед удалением делается резервная копия)",
                @"Claves vacías en HKCU\Software sin valores ni subclaves (se exporta una copia antes de borrar)" },
            new string[] { "Intel 驱动安装包", "Intel driver packages", "Intel-Treiberpakete", "Paquets de pilotes Intel", "Пакеты драйверов Intel", "Paquetes de controladores Intel" },
            new string[] { "NVIDIA 安装器残留", "NVIDIA installer leftovers", "NVIDIA-Installer-Reste", "Restes de l’installateur NVIDIA", "Остатки установщика NVIDIA", "Restos del instalador NVIDIA" },
            new string[] { "QQ 浏览器", "QQ Browser", "QQ Browser", "Navigateur QQ", "Браузер QQ", "Navegador QQ" },
            new string[] {
                "· 删除项直接永久删除，可在设置里改成移入回收站",
                "· Deletions are permanent; the Recycle Bin mode is a setting",
                "· Gelöschtes wird endgültig entfernt (Papierkorb-Modus ist eine Einstellung)",
                "· Les suppressions sont définitives (le mode corbeille est une option)",
                "· Удаление безвозвратное (режим корзины включается в настройках)",
                "· Lo borrado es definitivo (el modo papelera es una opción)" },
            new string[] {
                "所选项目将移入回收站（可恢复，但不释放磁盘空间）。",
                "Selected items go to the Recycle Bin (restorable, but no space is freed).",
                "Die Auswahl geht in den Papierkorb (wiederherstellbar, aber es wird kein Speicher frei).",
                "Les éléments vont à la corbeille (récupérables, mais aucun espace n’est libéré).",
                "Выбранное уйдёт в корзину (восстановимо, но место не освобождается).",
                "Los elementos irán a la papelera (recuperables, pero no libera espacio)." },
            new string[] {
                "回收站已被其他安全软件拦截，无法清空。请把本程序加入其信任区后重试。",
                "Another security product is blocking the Recycle Bin, so it cannot be emptied. Add this app to its trusted list and retry.",
                "Ein anderes Sicherheitsprogramm blockiert den Papierkorb. Fügen Sie dieses Programm der Vertrauensliste hinzu.",
                "Un autre logiciel de sécurité bloque la corbeille. Ajoutez ce programme à sa liste de confiance.",
                "Другой антивирус блокирует корзину. Добавьте программу в список доверенных.",
                "Otro programa de seguridad bloquea la papelera. Añade este programa a su lista de confianza." },
            new string[] {
                "已安排重启后清空回收站（重启即释放空间）。",
                "Recycle Bin emptying is queued for the next reboot (space is freed right after restart).",
                "Das Leeren des Papierkorbs ist für den nächsten Neustart eingeplant (Platz wird danach frei).",
                "Le vidage de la corbeille est programmé au prochain redémarrage (l’espace sera libéré ensuite).",
                "Очистка корзины запланирована на следующую перезагрузку (место освободится после неё).",
                "El vaciado de la papelera se ha programado para el próximo reinicio (el espacio se libera después)." },
            new string[] { "  ·  双击可打开目录确认", "  ·  double-click to open the folder", "  ·  Doppelklick öffnet den Ordner", "  ·  double-clic pour ouvrir le dossier", "  ·  двойной щелчок откроет папку", "  ·  doble clic para abrir la carpeta" },
            new string[] { "已在资源管理器中打开：", "Opened in Explorer: ", "Im Explorer geöffnet: ", "Ouvert dans l’Explorateur : ", "Открыто в Проводнике: ", "Abierto en el Explorador: " },
            new string[] { "目录不存在", "Folder not found", "Ordner nicht vorhanden", "Dossier introuvable", "Папки нет", "Carpeta inexistente" },
            new string[] { "该目录已经不存在了，可能已被清理。", "That folder no longer exists; it may already have been cleaned.", "Dieser Ordner existiert nicht mehr, evtl. schon bereinigt.", "Ce dossier n’existe plus, il a peut-être déjà été nettoyé.", "Этой папки больше нет — возможно, она уже очищена.", "Esa carpeta ya no existe; puede que ya se haya limpiado." },
            new string[] { "无法打开目录", "Cannot open the folder", "Ordner lässt sich nicht öffnen", "Impossible d’ouvrir le dossier", "Не удаётся открыть папку", "No se puede abrir la carpeta" },
            new string[] { "该项不是目录", "This item is not a folder", "Dieser Eintrag ist kein Ordner", "Cet élément n’est pas un dossier", "Это не папка", "Este elemento no es una carpeta" },
            new string[] {
                "注册表项与启动项没有对应目录；双击只对“孤儿安装目录”有效。",
                "Registry and startup items have no folder; double-click only works for leftover install folders.",
                "Registrierungs- und Autostart-Einträge haben keinen Ordner; der Doppelklick gilt nur für übrig gebliebene Installationsordner.",
                "Les entrées de registre et de démarrage n’ont pas de dossier ; le double-clic ne concerne que les dossiers d’installation résiduels.",
                "У разделов реестра и автозапуска нет папки; двойной щелчок работает только для оставшихся папок установки.",
                "Las entradas de registro e inicio no tienen carpeta; el doble clic solo funciona en carpetas de instalación sobrantes." },
            new string[] { " 下 · 应用列表里没有对应安装记录（按“设置-应用”核对）", " · no matching entry in the installed-apps list (checked against Settings > Apps)", " · kein Eintrag in der App-Liste (geprüft gegen Einstellungen > Apps)", " · aucune entrée dans la liste des applications installées (vérifié dans Paramètres > Applications)", " · нет записи в списке установленных программ (сверено с «Параметры → Приложения»)", " · no hay entrada en la lista de aplicaciones instaladas (verificado en Configuración > Aplicaciones)" },
            new string[] { " · 最近改动 ", " · last modified ", " · zuletzt geändert ", " · dernière modification ", " · последнее изменение ", " · última modificación " },
            new string[] { " 下 · 同名软件“", " · an app with the same name, \"", " · gleichnamige Software „", " · logiciel du même nom « ", " · одноимённая программа «", " · programa con el mismo nombre «" },
            new string[] { "”已在别处安装，此处疑似旧版本副本，请确认后再删", "\", is installed elsewhere; this folder looks like an old copy - check before deleting", "\", ist anderswo installiert; dieser Ordner sieht wie eine alte Kopie aus", "\", est installé ailleurs ; ce dossier ressemble à une ancienne copie", "\", установлена в другом месте; папка похожа на старую копию", "\" está instalado en otro lugar; esta carpeta parece una copia antigua" },
            new string[] { "npm 缓存", "npm cache", "npm-Cache", "cache npm", "кэш npm", "caché de npm" },
            new string[] {
                "下载目录中 3 天前的 msi/exe 安装包",
                "msi/exe installers in Downloads older than 3 days",
                "msi/exe-Installationspakete in „Downloads“, älter als 3 Tage",
                "installateurs msi/exe dans Téléchargements de plus de 3 jours",
                "установщики msi/exe в «Загрузках» старше 3 дней",
                "instaladores msi/exe en Descargas con más de 3 días" },
            new string[] { "已就绪，点「开始扫描」检查可清理项", "Ready. Click Start scan to check cleanable items.", "Bereit. Auf Scan starten klicken.", "Pret. Cliquez sur Lancer analyse.", "Готово. Нажмите Начать проверку.", "Listo. Pulsa Iniciar analisis." },
            new string[] { "开始扫描", "Start scan", "Scan starten", "Lancer analyse", "Начать проверку", "Iniciar analisis" },
            new string[] {
                "点上方「开始扫描」，检查完再逐项决定删除",
                "Click Start scan above; when the list appears, decide item by item.",
                "Oben auf Scan starten klicken; danach Eintrag fuer Eintrag entscheiden.",
                "Cliquez sur Lancer analyse ci-dessus, puis decidez element par element.",
                "Нажмите кнопку проверки выше, затем решайте по пунктам.",
                "Pulsa Iniciar analisis arriba y luego decide elemento por elemento." },
            new string[] {
                "卸载残留",
                "Leftover residue",
                "Programmreste",
                "Résidus de programmes",
                "Остатки программ",
                "Residuos de programas" },
            new string[] {
                "已卸载软件留下的空目录、空文件、空注册表键、旧目录与失效快捷方式；默认不勾选，逐组确认后再删",
                "Empty folders and files, empty registry keys, old app folders and dead shortcuts left by removed software. Unchecked by default; review each group before deleting.",
                "Leere Ordner und Dateien, leere Registrierungsschlüssel, alte Programmordner und tote Verknüpfungen deinstallierter Software. Standardmäßig nicht markiert; jede Gruppe vor dem Löschen prüfen.",
                "Dossiers et fichiers vides, clés de registre vides, anciens dossiers et raccourcis morts laissés par des logiciels désinstallés. Non cochés par défaut ; vérifiez chaque groupe avant suppression.",
                "Пустые папки и файлы, пустые ключи реестра, старые папки программ и нерабочие ярлыки от удалённых приложений. По умолчанию не отмечены; проверяйте каждую группу.",
                "Carpetas y archivos vacíos, claves de registro vacías, carpetas antiguas y accesos directos muertos de programas desinstalados. Sin marcar por defecto; revisa cada grupo." },
            new string[] {
                " 项）",
                " items)",
                " Einträge)",
                " éléments)",
                " шт.)",
                " elementos)" },
            new string[] {
                "扫描已卸载软件的残留...",
                "Scanning residue of removed programs...",
                "Programmreste werden geprüft...",
                "Analyse des résidus de programmes...",
                "Проверка остатков программ...",
                "Analizando residuos de programas..." },
        };

        public static void Fill(Dictionary<string, string> en, Dictionary<string, string> de,
            Dictionary<string, string> fr, Dictionary<string, string> ru, Dictionary<string, string> es)
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                string[] r = Rows[i];
                if (!en.ContainsKey(r[0])) en[r[0]] = r[1];
                if (!de.ContainsKey(r[0])) de[r[0]] = r[2];
                if (!fr.ContainsKey(r[0])) fr[r[0]] = r[3];
                if (!ru.ContainsKey(r[0])) ru[r[0]] = r[4];
                if (!es.ContainsKey(r[0])) es[r[0]] = r[5];
            }
        }
    }
}
