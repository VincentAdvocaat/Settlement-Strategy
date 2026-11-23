# Settlement Strategy

Bouw een nederzetting rond een rivier. Bezorgers vervoeren goederen over de wegen die je aanlegt. Bouwers voltooien wegen, gebouwen en uitbreidingen zodra het materiaal is aangekomen.

## Spelen

Open `project.godot` in **Godot 4.5.1 .NET**, met de **.NET 8 SDK** geïnstalleerd, en druk op F5. Kies **Campagne • De riviergrens** voor een duidelijk einddoel of **Vrij bouwen** om zonder campagnevoorwaarden door te spelen op dezelfde kaart.

1. Leg wegen aan vanaf het magazijn. Plaats het houthakkerskamp naast het bos en de steengroeve naast de rotsen. Verbind beide via de brug met het wegennet.
2. Plaats en verbind de zagerij. Bezorgers leveren 4 steen en 6 boomstammen voor de bouw. Een extra boomstam wordt de eerste plank.
3. Verbind een graanakker, molen en bakkerij. Het graan wordt meel, het meel wordt brood en het magazijn ontvangt brood voor de bewoners.
4. Bouw een woonhuis. Met stabiele broodvoorraad en vrije woonruimte trekt een bewoner in en komt er een bezorger bij.
5. Bouw en verbind een buitenpost. Selecteer die en klik **Troep sturen**. Voor de campagne heb je een eerste plank, minstens vier inwoners en drie tijdige maaltijden op rij bij de buitenpost nodig.

Je begint met 24 steen, 10 boomstammen, één bouwer en twee bezorgers. Bekijk voor het plaatsen de kosten onder de kaart. Sleep om een wegtracé te plannen en laat los om te bouwen. Selecteer een bouwplaats om voorraad, voortgang en de routes van betrokken bezorgers en bouwers te zien.

Onder **Meer bouwen** kun je een afgewerkte weg verharden, de opslag van een bestaand gebouw uitbreiden en een werkplaats bouwen. De werkplaats zet geleverde planken om in kennis. Gebruik die via **Onderzoek** voor karren die twee goederen tegelijk kunnen vervoeren of gewasrotatie die per oogst een extra graan oplevert.

## Pauze, opslaan en instellingen

Open **Escape** voor instellingen, opslaan of herstarten. Via het hoofdmenu laad je het opgeslagen spel. Er is één lokaal opslagvak. Laden gebruikt dezelfde versie van de speldata en mods als tijdens het opslaan.

In **Instellingen** vind je venstermodus, fullscreen op het huidige scherm, vensterresolutie, VSync, FPS-limiet, GUI-schaal, hoofdvolume, effectvolume en de bediening. Een gewijzigde schermmodus of resolutie moet binnen vijftien seconden worden bevestigd. De overige wijzigingen sla je op via **Toepassen**.

## Mods

Kopieer [barley.json](examples/mods/barley.json) naar `mods` in de gebruikersdatamap van de game en start een nieuw spel. Die map kun je in Godot openen via **Project → Open User Data Folder**. De gerstakker, mouterij en moutopslag verschijnen onder **Meer bouwen**. Verbind ze met een weg om gerst naar de mouterij en mout naar de opslag te vervoeren.

Een mod gebruikt hetzelfde formaat voor goederen, bouwkosten, opslag en recepten als [data/demo.json](data/demo.json). Geef goederen en gebouwen IDs die beginnen met de bestandsnaam en een dubbele punt, bijvoorbeeld `barley:malt`. Een ongeldige mod geeft een melding in het menu met het bestand en de fout in het logboek.

## Exporteren

Installeer de bijpassende .NET-exporttemplates in Godot. Het project bevat presets voor Windows x64 en Linux x64. Houd alle geëxporteerde bestanden bij elkaar.
