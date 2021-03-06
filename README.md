# Screen Space Mulit Scatter for Unity's Post Processing Stack v2
Post Processing Effect that mimics light getting scattered though fog for your moody atmosphere or cold morning scene or whatever you can think of.

Based on the [Keijiro Takahashi's Streak Effect](https://github.com/keijiro/Kino) and [SSMS by OCASM](https://github.com/OCASM/SSMS).

If you use this effect in your project, feel free to let us know on [twitter](https://twitter.com/byteslider).

Examples
-

No fog
![screenshot1](./ReadMe/NoFog.webp)

Built-in Fog
![screenshot2](./ReadMe/Fog.webp)

Built-in Fog + SSMS
![screenshot3](./ReadMe/FogSsms.webp)

Built-in Fog + SSMS + Bloom
![screenshot4](./ReadMe/FogSsmsBloom.webp)

Known Issues
-
The effect will not enclose transparent objects. That avoids artifact with TAA and transparent object disappearing in the fog, even if they're in the foreground. This can be changed by setting ```PostProcessEvent.BeforeTransparent``` to ```PostProcessEvent.BeforeStack```. 
