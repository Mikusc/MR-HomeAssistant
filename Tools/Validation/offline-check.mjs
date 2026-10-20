import { readFileSync, writeFileSync, mkdirSync, readdirSync, existsSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const project = process.cwd();
const protocolOnly = process.argv.includes('--protocol-only');
const editor = process.env.UNITY_CONTENTS || '/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents';
const scripting = path.join(editor, 'Resources/Scripting');
const dotnet = path.join(scripting, 'NetCoreRuntime/dotnet');
const compiler = path.join(scripting, 'DotNetSdkRoslyn/csc.dll');
const output = path.join(project, 'Library/MRHomeAssistantValidation');
mkdirSync(output, { recursive: true });
function run(args) {
  const result = spawnSync(dotnet, args, { cwd: project, stdio: 'inherit' });
  if (result.status !== 0) process.exit(result.status || 1);
}
function sources(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap(item => item.isDirectory()
    ? sources(path.join(directory, item.name))
    : item.name.endsWith('.cs') ? [path.join(directory, item.name)] : []);
}
function unityResponse(name) {
  const artifacts = path.join(project, 'Library/Bee/artifacts');
  const candidates = existsSync(artifacts)
    ? readdirSync(artifacts).map(directory => path.join(artifacts, directory, name))
      .filter(file => existsSync(file))
    : [];
  if (candidates.length !== 1) {
    throw new Error(`Expected one current ${name} under Library/Bee/artifacts. ` +
      'Open this project in Unity and let it compile, or use --protocol-only for the independent protocol checks.');
  }
  return readFileSync(candidates[0], 'utf8');
}
if (!protocolOnly) {
  const response = unityResponse('Assembly-CSharp.rsp')
    .split(/\r?\n/).filter(line => !line.startsWith('-out:') && !line.startsWith('-refout:') &&
      !/^"Assets\//.test(line) && !line.startsWith('-analyzer:') && !line.startsWith('/additionalfile:'));
  response.push(...sources('Assets/Scripts/Runtime').map(file => JSON.stringify(file)));
  response.push('-out:' + JSON.stringify(path.join(output, 'Runtime.dll')));
  writeFileSync(path.join(output, 'runtime.rsp'), response.join('\n'));
  run([compiler, '@' + path.join(output, 'runtime.rsp')]);
  console.log('PASS: runtime C# compilation against cached Unity project references');
  const editorResponse = unityResponse('Assembly-CSharp-Editor.rsp')
    .split(/\r?\n/).filter(line => !line.startsWith('-out:') && !line.startsWith('-refout:') &&
      !/^"Assets\//.test(line) && !line.startsWith('-analyzer:') && !line.startsWith('/additionalfile:'))
    .map(line => line.includes('/Assembly-CSharp.ref.dll') ? '-r:' + JSON.stringify(path.join(output, 'Runtime.dll')) : line);
  editorResponse.push(...sources('Assets/Editor').map(file => JSON.stringify(file)));
  editorResponse.push('-out:' + JSON.stringify(path.join(output, 'EditorChecks.dll')));
  writeFileSync(path.join(output, 'editor.rsp'), editorResponse.join('\n'));
  run([compiler, '@' + path.join(output, 'editor.rsp')]);
  console.log('PASS: UI capture/verification editor tool compilation');
  if (process.argv.includes('--compile-only')) process.exit(0);
}

const frameworkRoot = path.join(scripting, 'NetCoreRuntime/shared/Microsoft.NETCore.App');
const version = readdirSync(frameworkRoot).filter(name => /^8\./.test(name)).sort().at(-1);
const framework = path.join(frameworkRoot, version);
const refs = readdirSync(framework).filter(name => name.endsWith('.dll')).map(name => '-r:' + JSON.stringify(path.join(framework, name)));
const files = [
  'Assets/Scripts/Runtime/Application/HomeAssistant/HomeAssistantConnection.cs',
  'Assets/Scripts/Runtime/Application/HomeAssistant/HomeAssistantModels.cs',
  'Assets/Scripts/Runtime/Application/HomeAssistant/HomeAssistantDevices.cs',
  'Assets/Scripts/Runtime/Application/HomeAssistant/HomeAssistantCatalog.cs',
  'Assets/Scripts/Runtime/Application/Settings/AppSettingsData.cs',
  'Assets/Scripts/Runtime/Infrastructure/Persistence/AppSettingsRepository.cs',
  'Assets/Scripts/Runtime/Infrastructure/Persistence/DashboardPreferencesRepository.cs',
  'Tools/Validation/UnityFacade.cs', 'Tools/Validation/ProtocolChecks.cs'
];
const executable = path.join(output, 'ProtocolChecks.dll');
writeFileSync(path.join(output, 'checks.rsp'), [
  '-nologo', '-nowarn:0649', '-target:exe', '-langversion:latest', '-out:' + JSON.stringify(executable), ...refs, ...files.map(file => JSON.stringify(file))
].join('\n'));
writeFileSync(path.join(output, 'ProtocolChecks.runtimeconfig.json'), JSON.stringify({
  runtimeOptions: { tfm: 'net8.0', framework: { name: 'Microsoft.NETCore.App', version } }
}));
run([compiler, '@' + path.join(output, 'checks.rsp')]);
run([executable, ...(process.argv.includes('--serve') ? ['--serve'] : [])]);
