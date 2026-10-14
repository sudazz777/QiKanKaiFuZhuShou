using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

internal static class HostBootstrap {
 internal const string NewServerConfig="{\r\n  \"saveName\": \"\",\r\n  \"autosaveMinutes\": 5,\r\n  \"password\": \"\",\r\n  \"port\": 4200,\r\n  \"logFile\": true,\r\n  \"steam\": true,\r\n  \"traceTick\": false,\r\n  \"tracePublish\": false,\r\n  \"traceBandits\": false\r\n}\r\n";
 static readonly Encoding Utf8=new UTF8Encoding(false);
 static readonly string[] RequiredServerFiles={"engine\\dotnet\\dotnet.exe","engine\\bin\\Win64_Shipping_Server\\TaleWorlds.Starter.DotNetCore.exe","engine\\bin\\Win64_Shipping_Server\\TaleWorlds.Starter.DotNetCore.dll","engine\\bin\\Win64_Shipping_Server\\TaleWorlds.Starter.DotNetCore.runtimeconfig.json","engine\\Modules\\Coop\\bin\\Win64_Shipping_Server\\Coop.Core.dll","engine\\Modules\\Coop\\bin\\Win64_Shipping_Server\\GameInterface.dll","engine\\Modules\\Coop\\bin\\Win64_Shipping_Server\\Common.dll","engine\\Modules\\Coop\\bin\\Win64_Shipping_Server\\Coop.Steam.dll","EULA.md","LICENSE.txt"};
 static bool Present(string path){try{return File.Exists(path)&&new FileInfo(path).Length>0;}catch{return false;}}
 internal static string ModTemplate(HostInstallation install){if(string.IsNullOrEmpty(install.ServerDir))return "";return new[]{Path.Combine(install.ServerDir,"engine","Modules","Coop","mod-config.default.json"),Path.Combine(install.ServerDir,"server-data","mod-config.json"),Path.Combine(Path.GetDirectoryName(install.ServerDir),"mod-config.default.json")}.FirstOrDefault(Present)??"";}
 internal static string WorldTemplate(HostInstallation install){if(string.IsNullOrEmpty(install.ServerDir))return "";return new[]{Path.Combine(install.ServerDir,"server-data","Game Saves","default_new_game.sav"),Path.Combine(install.ServerDir,"engine","bin","Win64_Shipping_Server","default_new_game.sav")}.FirstOrDefault(Present)??"";}
 internal static string NativeVersion(string moduleRoot){try{string file=Path.Combine(moduleRoot,"Native","SubModule.xml");if(!Present(file))return "";var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};var doc=new XmlDocument{XmlResolver=null};using(var reader=XmlReader.Create(file,settings))doc.Load(reader);var node=doc.SelectSingleNode("/Module/Version/@value");return node==null?"":node.Value.Trim().TrimStart('v','V');}catch{return "";}}
 internal static string[] Inspect(HostInstallation install) {
  var errors=new List<string>(install.Validate());
  try {
   if(!string.IsNullOrEmpty(install.ServerDir))foreach(string file in RequiredServerFiles)if(!Present(Path.Combine(install.ServerDir,file)))errors.Add("服务器安装不完整，缺少："+file+"。请安装或重新下载完整 DedicatedServer 文件夹。");
   string mod=ModTemplate(install),world=WorldTemplate(install);
   if(mod=="")errors.Add("找不到服务器配套玩法配置模板，请修复 Coop MOD。");else ValidateMod(File.ReadAllText(mod,Utf8));
   if(world=="")errors.Add("找不到服务器初始世界模板，请修复 Coop MOD。");
   string existingTemplate=Path.Combine(install.SavesDir,"default_new_game.sav");if(File.Exists(existingTemplate)&&!Present(existingTemplate))errors.Add("本机初始世界模板为空或无法读取，请从完整服务器包恢复 default_new_game.sav 后重新适配；已有战役仍保留。");
   string gameVersion=string.IsNullOrEmpty(install.GameDir)?"":NativeVersion(Path.Combine(install.GameDir,"Modules"));
   string engineVersion=string.IsNullOrEmpty(install.ServerDir)?"":NativeVersion(Path.Combine(install.ServerDir,"engine","Modules"));
   if(gameVersion=="")errors.Add("无法读取游戏版本，请选择完整游戏目录。");
   if(engineVersion=="")errors.Add("无法读取专用服务器的游戏版本，请选择完整 DedicatedServer 文件夹。");
   if(gameVersion!=""&&engineVersion!=""&&gameVersion!=engineVersion)errors.Add("游戏版本 "+gameVersion+" 与服务器版本 "+engineVersion+" 不匹配，请在 Steam 中调整游戏版本或更新配套 MOD。");
   if(File.Exists(install.ConfigPath))ValidateServer(File.ReadAllText(install.ConfigPath,Utf8));
   if(File.Exists(install.ModConfigPath))ValidateMod(File.ReadAllText(install.ModConfigPath,Utf8));
  }catch(Exception error){errors.Add("配置或路径检查未通过："+error.Message);}
  return errors.Distinct().ToArray();
 }
 internal static void ValidateServer(string config) {
  Jsonc.Parse(config);var name=Jsonc.Scalar(config,"saveName") as string;if(name==null||(name!=""&&!CoopLauncher.ValidSaveName(name)))throw new Exception("服务器 saveName 无效，请修正配置；原文件已保留。");
  object password=Jsonc.Scalar(config,"password");if(!(password is string))throw new Exception("服务器密码字段格式不兼容。");CoopLauncher.ValidatePassword((string)password);
  int minutes=Convert.ToInt32(Jsonc.Scalar(config,"autosaveMinutes")),port=Convert.ToInt32(Jsonc.Scalar(config,"port"));if(minutes<0||minutes>1440||port<1||port>65535)throw new Exception("自动保存间隔或端口超出支持范围。");
  foreach(string key in new[]{"steam","logFile"})if(!(Jsonc.Scalar(config,key) is bool))throw new Exception("服务器字段格式不兼容："+key);
 }
 internal static void ValidateMod(string config) {
  Jsonc.Parse(config);foreach(var key in CoopLauncher.DifficultyKeys)if(!new[]{"VeryEasy","Easy","Realistic"}.Contains(Jsonc.Scalar(config,key) as string))throw new Exception("MOD 难度字段不兼容："+key);
  foreach(var key in new[]{"birthAndDeath","autoAllocateClanMemberPerks"})if(!(Jsonc.Scalar(config,key) is bool))throw new Exception("MOD 开关字段不兼容："+key);
 }
 internal static void Prepare(HostInstallation install) {
  string[] errors=Inspect(install);if(errors.Length>0)throw new Exception(string.Join("\r\n",errors));
  Directory.CreateDirectory(install.SavesDir);ProbeWritable(install.DataDir);ProbeWritable(Path.GetDirectoryName(install.ModConfigPath));
  CreateMissing(install.ConfigPath,Utf8.GetBytes(NewServerConfig));
  CreateMissing(install.ModConfigPath,File.ReadAllBytes(ModTemplate(install)));
  CreateMissing(Path.Combine(install.SavesDir,"default_new_game.sav"),File.ReadAllBytes(WorldTemplate(install)));
  ValidateServer(File.ReadAllText(install.ConfigPath,Utf8));ValidateMod(File.ReadAllText(install.ModConfigPath,Utf8));
 }
 static void ProbeWritable(string folder) {
  string probe=Path.Combine(folder,"launcher-write-check-"+Guid.NewGuid().ToString("N")+".tmp");
  try{using(var file=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.WriteByte(0);file.Flush(true);}}catch(Exception error){throw new Exception("无法写入本机存档目录："+folder+"\r\n"+error.Message);}finally{if(File.Exists(probe))File.Delete(probe);}
 }
 static void CreateMissing(string path,byte[] bytes) {
  if(File.Exists(path))return;Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".new-"+Guid.NewGuid().ToString("N")+".tmp";
  try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}try{File.Move(temp,path);}catch(IOException){if(!File.Exists(path))throw;}}
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
 internal static void Check(string reportPath,List<string> results) {
  string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)),"b-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(root);
  var install=new HostInstallation{SteamExe=Path.Combine(root,"Steam","Steam.exe"),GameDir=Path.Combine(root,"游戏"),ServerDir=Path.Combine(root,"s"),DocumentsOverride=Path.Combine(root,"OneDrive","中文文档")};
  Fixture(install.SteamExe,"fixture");Fixture(Path.Combine(install.GameDir,"bin","Win64_Shipping_Client","Bannerlord.exe"),"fixture");Fixture(Path.Combine(install.ServerDir,"BannerlordCoopServer.exe"),"fixture");Fixture(Path.Combine(install.ModuleBin,"Steamworks.NET.dll"),"fixture");Fixture(Path.Combine(install.ModuleBin,"steam_api64.dll"),"fixture");foreach(string file in RequiredServerFiles)Fixture(Path.Combine(install.ServerDir,file),"fixture");
  string xml="<Module><Version value=\"v1.4.8\"/></Module>";Fixture(Path.Combine(install.GameDir,"Modules","Native","SubModule.xml"),xml);Fixture(Path.Combine(install.ServerDir,"engine","Modules","Native","SubModule.xml"),xml);
  var difficulty=new Dictionary<string,object>();foreach(var key in CoopLauncher.DifficultyKeys)difficulty[key]="VeryEasy";difficulty["birthAndDeath"]=false;difficulty["autoAllocateClanMemberPerks"]=false;string template=new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"difficulty",difficulty},{"otherModSetting","preserved"}});
  Fixture(Path.Combine(install.ServerDir,"engine","Modules","Coop","mod-config.default.json"),template);Fixture(Path.Combine(install.ServerDir,"server-data","Game Saves","default_new_game.sav"),"world fixture");
  Prepare(install);if(!File.Exists(install.ConfigPath)||!File.Exists(install.ModConfigPath)||!File.Exists(Path.Combine(install.SavesDir,"default_new_game.sav"))||File.Exists(Path.Combine(install.DataDir,"eula.txt")))throw new Exception("First-run data initialization failed");if((string)Jsonc.Scalar(File.ReadAllText(install.ConfigPath),"saveName")!="")throw new Exception("First-run initialization unexpectedly selected a world");results.Add("PASS 新电脑首次创建本机配置、配套世界模板；没有选档不自动开服，不迁移旧电脑协议记录");
  string parentBanner=Environment.GetEnvironmentVariable("BANNERLORD_USER_DIR"),parentCoop=Environment.GetEnvironmentVariable("COOP_DATA_DIR");var process=CoopLauncher.ServerProcess(install);if(process.EnvironmentVariables["BANNERLORD_USER_DIR"]!=install.DataDir||process.EnvironmentVariables["COOP_DATA_DIR"]!=Path.GetDirectoryName(install.ModConfigPath)||process.UseShellExecute||process.CreateNoWindow||process.WorkingDirectory!=install.ServerDir||!process.Arguments.Contains("--data-dir \""+install.DataDir+"\"")||Environment.GetEnvironmentVariable("BANNERLORD_USER_DIR")!=parentBanner||Environment.GetEnvironmentVariable("COOP_DATA_DIR")!=parentCoop)throw new Exception("Child data paths or environment isolation incorrect");results.Add("PASS 启动使用适配后的服务器、存档与难度路径，仅对子进程指定位置，保留系统环境");
  string customized=Jsonc.Set(Jsonc.Set(NewServerConfig,"saveName","已有世界"),"password","keep-me");Fixture(install.ConfigPath,customized);Fixture(install.ModConfigPath,template+"\r\n// existing configuration");Fixture(Path.Combine(install.SavesDir,"已有世界.sav"),"existing world");Fixture(Path.Combine(install.SavesDir,"已有世界.json"),"existing players");string existingMod=File.ReadAllText(install.ModConfigPath);Prepare(install);if(File.ReadAllText(install.ConfigPath)!=customized||File.ReadAllText(install.ModConfigPath)!=existingMod||File.ReadAllText(Path.Combine(install.SavesDir,"已有世界.sav"))!="existing world"||File.ReadAllText(Path.Combine(install.SavesDir,"已有世界.json"))!="existing players")throw new Exception("Existing user data changed during adaptation");results.Add("PASS 重复适配保留既有存档、角色、密码和玩法设置，文档目录支持中文及重定向");
  Fixture(Path.Combine(install.GameDir,"Modules","Native","SubModule.xml"),"<Module><Version value=\"v1.3.0\"/></Module>");if(!Inspect(install).Any(x=>x.Contains("不匹配")))throw new Exception("Game version mismatch accepted");Fixture(Path.Combine(install.GameDir,"Modules","Native","SubModule.xml"),xml);File.Delete(Path.Combine(install.ServerDir,RequiredServerFiles[0]));if(!Inspect(install).Any(x=>x.Contains("安装不完整")))throw new Exception("Incomplete standalone package accepted");results.Add("PASS 游戏和服务器版本不匹配、缺少完整服务器运行环境时禁止适配及开服");
  var actual=HostInstallation.Detect();string[] actualErrors=Inspect(actual);results.Add("READ-ONLY 当前电脑安装与配置适配检查："+(actualErrors.Length==0?"通过":string.Join("；",actualErrors)));
 }
 static void Fixture(string path,string text){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,text,Utf8);}
}
