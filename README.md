# Settlement Strategy

Een korte speelbare bouwdemo. Leg wegen, win steen en boomstammen en lever beide aan een zagerij in aanbouw. De proef is klaar zodra de zagerij haar eerste plank maakt.

## Spelen

Het magazijn start met 24 steen en 10 boomstammen. Een wegtegel kost 1 steen, een steengroeve 2 steen en 3 boomstammen, en een houthakkerskamp 1 steen en 2 boomstammen. Twee bezorgers vervoeren het materiaal over afgewerkte wegen. Eén bouwer loopt naar een bereikbaar bouwterrein zodra het materiaal er is. Een uitgestippelde weg wordt tegel voor tegel bereikbaar naarmate de bouw vordert.

Pak de desktopdownload uit en open het uitvoerbare bestand. Laat de bestanden uit het archief bij elkaar staan. Op Linux kan het nodig zijn het uitvoerbare bestand eerst uitvoerrechten te geven.

1. Kies **Houthakker** en plaats het kamp op gras naast het bos.
2. Kies **Steengroeve** en plaats de groeve op gras naast de rotsen, aan de andere kant van de rivier.
3. Verbind beide gebouwen via de brug met de weg bij de voorraadschuur. Met **Wegen** kun je klikken of slepen.
4. Plaats de **Zagerij** en sluit ook haar bouwterrein aan. Selecteer het gebouw om de leveringen te volgen.
5. Wacht tot beide dragers de bouwkosten hebben geleverd. De werkende zagerij vraagt vervolgens een extra boomstam en maakt daarmee de eerste plank.

Gebruik **Pauze**, **3×** of **Herstart** om de proef te bekijken en te herhalen.

## Ontwikkelen

Open `project.godot` in **Godot 4.5.1 .NET** met de **.NET 8 SDK** geïnstalleerd. Druk op F5 om te spelen. De originele placeholdergraphics worden getekend door Godot. In `data/demo.json` staan bouwkosten, recepten, opslag en transportprioriteiten. In de editor lees je gewijzigde speldata in met **Herstart**. Voor het exporteren van aangepaste data installeer je de .NET-exporttemplates en exporteer je de build opnieuw.
