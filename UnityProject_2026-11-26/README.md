# Robot Fatal Shooter

FPS tatico em Unity com partidas contra bots e modo online.

## Projeto

- Unity `6000.6.8f1`
- URP 17.6
- Importador de modelos GLB: `com.unity.cloud.gltfast` 6.21.0
- Os 12 agentes ficam em `Assets/Resources/Agents`; modelos GLB e retratos estao em subpastas de `Resources`.

## Controles

| Tecla | Acao |
| --- | --- |
| WASD | Mover |
| Mouse | Olhar |
| Botao esquerdo / direito | Atirar / mirar |
| Shift / Espaco / Ctrl | Correr / pular / agachar |
| 1–5 ou roda | Trocar de arma |
| E | Dash |
| Q | Habilidade principal ou granada |
| C | Segunda utilidade, quando o agente tem uma |
| X | Ultimate quando a carga chega a 100% |
| R / F | Recarregar / interagir |

Os agentes Jason, Todd, Kate, Harold, Death Girl, Aurora e Brutus tem habilidades proprias. Os outros cinco preservam o kit generico atual. Q e C recarregam com tempo; X carrega durante a partida.

## Publicacao

O build WebGL e gerado pelo Unity e publicado em `Build/play`. Essa pasta e ignorada no Git para manter o repositorio focado no projeto-fonte; os arquivos `.glb` usam Git LFS.
