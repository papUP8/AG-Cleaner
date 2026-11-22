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
                @"HKCU\Software 下既没有值也没有子键的空壳键（删除前自动导出备份，软件会按需重建）",
                @"Shell keys under HKCU\Software with no values and no subkeys (exported as a backup first; apps recreate them when needed)",
                @"Leere Schlüssel unter HKCU\Software ohne Werte und Unterschlüssel (vor dem Löschen gesichert)",
                @"Clés vides sous HKCU\Software sans valeur ni sous-clé (sauvegardées avant suppression)",
                @"Пустые разделы в HKCU\Software без значений и подразделов (перед удалением делается резервная копия)",
                @"Claves vacías en HKCU\Software sin valores ni subclaves (se exporta una copia antes de borrar)" },
            new string[] { "Intel 驱动安装包", "Intel driver packages", "Intel-Treiberpakete", "Paquets de pilotes Intel", "Пакеты драйверов Intel", "Paquetes de controladores Intel" },
            new string[] { "NVIDIA 安装器残留", "NVIDIA installer leftovers", "NVIDIA-Installer-Reste", "Restes de l’installateur NVIDIA", "Остатки установщика NVIDIA", "Restos del instalador NVIDIA" },
            new string[] { "QQ 浏览器", "QQ Browser", "QQ Browser", "Navigateur QQ", "Браузер QQ", "Navegador QQ" }
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
