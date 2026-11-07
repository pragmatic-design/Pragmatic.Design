/**
 * The check that keeps the Showcase's module projects on each engine free of EF Core and their exclusions to the allowed list.
 *
 *   node --test scripts/engine-projects.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { engineProjectProblems, engineOf } from './engine-projects.mjs';

const DIR = 'examples/showcase/engines';
const project = (name, body) => ({ path: `${DIR}/${name}/${name}.csproj`, text: `<Project>\n${body}\n</Project>` });
const files = {
  'Pragmatic.Persistence/src/Pragmatic.Persistence.Sql/Pragmatic.Persistence.Sql.csproj': '<Project><ProjectReference Include="..\\Pragmatic.Persistence\\Pragmatic.Persistence.csproj" /></Project>',
  'Pragmatic.Persistence/src/Pragmatic.Persistence/Pragmatic.Persistence.csproj': '<Project></Project>',
  'Pragmatic.Messaging/src/Pragmatic.Messaging.EFCore/Pragmatic.Messaging.EFCore.csproj': '<Project><PackageReference Include="Microsoft.EntityFrameworkCore.Relational" /></Project>',
  'Pragmatic.Messaging/src/Pragmatic.Messaging.Wrapper/Pragmatic.Messaging.Wrapper.csproj': '<Project><ProjectReference Include="..\\Pragmatic.Messaging.EFCore\\Pragmatic.Messaging.EFCore.csproj" /></Project>',
};
const read = (path) => files[path] ?? null;
const exists = () => true;
const sqlBridge = '<ItemGroup><ProjectReference Include="..\\..\\..\\..\\Pragmatic.Persistence\\src\\Pragmatic.Persistence.Sql\\Pragmatic.Persistence.Sql.csproj" /></ItemGroup>';

test('a module project on its engine, with an allowed exclusion under its comment: nothing to report', () => {
  const p = project('Showcase.Booking.Documents', `${sqlBridge}
  <!-- Refused by the documents engine by design. -->
  <ItemGroup>
    <Compile Remove="..\\..\\src\\Showcase.Booking\\Reservations\\Actions\\GetReservationAction.cs" />
  </ItemGroup>`);
  assert.deepEqual(engineProjectProblems([p], read, exists), []);
});

test('a project reaching EF Core through another is reported, with the way it was reached', () => {
  const p = project('Showcase.Booking.Sql', '<ItemGroup><ProjectReference Include="..\\..\\..\\..\\Pragmatic.Messaging\\src\\Pragmatic.Messaging.Wrapper\\Pragmatic.Messaging.Wrapper.csproj" /></ItemGroup>');
  assert.deepEqual(engineProjectProblems([p], read, exists), [
    `${p.path} reaches EF Core: Pragmatic.Messaging.Wrapper.csproj -> Pragmatic.Messaging.EFCore.csproj`,
  ]);
});

test('a package two references down is found too', () => {
  const p = project('Showcase.Booking.Sql', '<ItemGroup><ProjectReference Include="..\\..\\..\\..\\Pragmatic.Messaging\\src\\Pragmatic.Messaging.EFCore\\Pragmatic.Messaging.EFCore.csproj" /></ItemGroup>');
  const viaPackage = { ...p, text: p.text.replace('Pragmatic.Messaging.EFCore\\Pragmatic.Messaging.EFCore.csproj', 'Pragmatic.Messaging.Wrapper\\Pragmatic.Messaging.Wrapper.csproj') };
  const readNoName = (path) => (path.endsWith('Wrapper.csproj') ? '<Project><ProjectReference Include="..\\Pragmatic.Persistence.Sql\\Plain.csproj" /></Project>' : path.endsWith('Plain.csproj') ? '<Project><PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" /></Project>' : null);
  assert.deepEqual(engineProjectProblems([viaPackage], readNoName, exists), [
    `${p.path} reaches EF Core: Pragmatic.Messaging.Wrapper.csproj -> Plain.csproj -> Microsoft.EntityFrameworkCore.SqlServer`,
  ]);
});

test('a project referencing an EF Core package itself is reported', () => {
  const p = project('Showcase.Booking.Sql', '<ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore" /></ItemGroup>');
  assert.deepEqual(engineProjectProblems([p], read, exists), [`${p.path} reaches EF Core: Microsoft.EntityFrameworkCore`]);
});

test('a file left out that is not on the list is reported: it is an issue, not an exclusion', () => {
  const p = project('Showcase.Billing.Sql', `<!-- why -->
  <ItemGroup><Compile Remove="..\\..\\src\\Showcase.Billing\\Invoices\\Actions\\ExportInvoicesAction.cs" /></ItemGroup>`);
  assert.match(engineProjectProblems([p], read, exists)[0], /ExportInvoicesAction\.cs, which is not an allowed exclusion on the Sql engine/);
});

test('an exclusion allowed on the documents engine only is reported on the Sql one', () => {
  const p = project('Showcase.Booking.Sql', `<!-- why -->
  <ItemGroup><Compile Remove="..\\..\\src\\Showcase.Booking\\Reservations\\Actions\\GetReservationAction.cs" /></ItemGroup>`);
  assert.match(engineProjectProblems([p], read, exists)[0], /not an allowed exclusion on the Sql engine/);
});

test('an exclusion with no comment saying why is reported', () => {
  const p = project('Showcase.Booking.Documents', `<!-- the bridge -->
  <ItemGroup><ProjectReference Include="x.csproj" /></ItemGroup>
  <ItemGroup><Compile Remove="..\\..\\src\\Showcase.Booking\\Reservations\\Actions\\GetReservationAction.cs" /></ItemGroup>`);
  assert.match(engineProjectProblems([p], read, exists)[0], /with no comment saying why/);
});

test('an exclusion of a file that does not exist is reported: the list has gone stale', () => {
  const p = project('Showcase.Booking.Documents', `<!-- why -->
  <ItemGroup><Compile Remove="..\\..\\src\\Showcase.Booking\\Reservations\\Actions\\GetReservationAction.cs" /></ItemGroup>`);
  assert.match(engineProjectProblems([p], read, () => false)[0], /which does not exist/);
});

// The build properties the generator reads (#582): what turns a feature on in the source project turns it on on the engine too.
const linked = '<ItemGroup><Compile Include="..\\..\\src\\Showcase.Booking\\**\\*.cs" LinkBase="Booking" /></ItemGroup>';
const source = (property) => (path) => (path === 'examples/showcase/src/Showcase.Booking/Showcase.Booking.csproj'
  ? `<Project><PropertyGroup>${property}</PropertyGroup></Project>` : read(path));

test('an engine project that does not set a generator property its source project sets is reported', () => {
  const p = project('Showcase.Booking.Sql', linked);
  assert.deepEqual(engineProjectProblems([p], source('<PragmaticGenerateJsonContext>true</PragmaticGenerateJsonContext>'), exists), [
    `${p.path} sets PragmaticGenerateJsonContext to (unset) where examples/showcase/src/Showcase.Booking/Showcase.Booking.csproj sets it to true: the generator emits different code on the two`,
  ]);
});

test('an engine project that sets what its source project sets: nothing to report', () => {
  const p = project('Showcase.Booking.Sql', `<PropertyGroup><PragmaticGenerateJsonContext>true</PragmaticGenerateJsonContext></PropertyGroup>${linked}`);
  assert.deepEqual(engineProjectProblems([p], source('<PragmaticGenerateJsonContext>true</PragmaticGenerateJsonContext>'), exists), []);
});

test('the engine is the project name\'s suffix', () => {
  assert.equal(engineOf('a/Showcase.Booking.Sql.csproj'), 'Sql');
  assert.equal(engineOf('a\\Showcase.Booking.Documents.csproj'), 'Documents');
  assert.equal(engineOf('a/Showcase.Booking.csproj'), null);
});
