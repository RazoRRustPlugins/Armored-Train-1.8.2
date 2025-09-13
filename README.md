<img width="1920" height="1080" alt="train(1)" src="https://github.com/user-attachments/assets/40bff991-5d74-42d7-97a2-41b2e18e6e45" />

**About Armored Train**

**New Year Update**

The  New Year  is coming and I have prepared for you a new preset customization for the train!After installing the update, customization will be automatically applied to all cars in the configuration of the armored train. To disable this, make the "Customization preset" parameter empty in the config.
You can also disable the customization of certain wagons or change the customization of the NPCs in the file oxide/data/ArmoredTrain/NewYear 

**Halloween Update**

Have you already prepared your server for Halloween?
On the eve of the Halloween Rust update, I have prepared for customization of all armored train cars.
After installing the update, customization will be automatically applied to all cars in the configuration of the armored train. To disable this, make the "Customization preset" parameter empty in the config. 

This plugin will add an armored train to your server, which can travel both in the subway and by rail.
It can consist of any number of wagons. A helicopter can accompany it.
Bradley, npc, turrets, simsites can be installed on the train.
The plugin allows you to create any number of train presets, for which the order of wagons, the helicopter preset, the probability of spawn and the duration of patrolling can be specified. Each wagon or locomotive can be configured separately. 
Any speed can be set by the locomotive.If there is no spawn of trains on the surface on your custom map, read the section "Custom spawn points”

**Custom spawn points**

If you are using a custom map in which there are no spawn trains on the surface, but there is a railway, then for the train to work on the surface, you need to add custom spawn points of the train.
**Instruction:**

    Stand at the point where you want the train to spawn
    Enter the command /atrainpoint
    If you receive a message that a point has been created, enable "Use custom spawn coordinates [true/false]” in config

For correct operation, it is recommended to create several spawn points of the train

**Chat commands (only for administrators)**

    /atrainstart– launches the event in a random configuration
    /atrainstartunderground - forcibly launches an event underground
    /atrainstartaboveground - forcibly launches an event aboveground
    /atrainstart <trainPresetName> –   launches the event in the <trainPresetName> configuration
    /atrainstartunderground  <trainPresetName>
    /atrainstartaboveground  <trainPresetName>
    /atrainstop– stops the event
    /atrainpoint– creates a custom spawn point of the train in your position

**Console commands (RCON only)**

    atrainstart– launches the event in a random configuration
    atrainstartunderground - forcibly launches an event underground
    atrainstartaboveground - forcibly launches an event aboveground
    atrainstart <trainPresetName> –   launches the event in the <trainPresetName> configuration
    atrainstartunderground <trainPresetName>
    atrainstartaboveground <trainPresetName>
    atrainstop – stops the event

**Config**

    en  –  example of plugin configuration in English
    ru  –  example of plugin configuration in Russian

**Dependencies (optional, not required)**

    True PVE
    PveMode
    GUI Announcements
    Notify
    DiscordMessages
    AlphaLoot
    CustomLoot
    Economics
    Server Rewards
    IQEconomic

**Api**

    bool  IsArmoredTrainActive()
    bool  StopArmoredTrain()
    bool  StartArmoredTrainEvent()
    bool  EndArmoredTrainEvent()
    bool IsTrainBradley(uint netID)  
    bool IsTrainHeli(uint netID)  
    bool IsTrainCrate(uint netID)  
    bool IsTrainSamSite(uint netID)  
    bool IsTrainWagon(uint netID)  
    bool IsTrainTurret(uint netID)  
    Vector3 ArmoredTrainLocomotivePosition()

**Hooks**

    OnArmoredTrainEventStart
    OnArmoredTrainEventStop
