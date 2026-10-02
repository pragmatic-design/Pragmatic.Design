---
title: "Getting Started"
description: "```xml"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Notes/docs/getting-started.md
sidebar:
  order: 2
---
## 1. Install the package

```xml
<PackageReference Include="Pragmatic.Notes" />
```

Typical companion packages:

```xml
<PackageReference Include="Pragmatic.Persistence" />
<PackageReference Include="Pragmatic.Endpoints" />
```

## 2. Mark the entity

```csharp
[Entity]
[Resource("reservations")]
[HasNotes(MaxLength = 4000, EditWindowMinutes = 60)]
public partial class Reservation
{
    public string GuestName { get; set; } = "";
}
```

## 3. Build and use

The generated note feature provides internal note actions and, when endpoints are enabled, note routes under the parent entity.

