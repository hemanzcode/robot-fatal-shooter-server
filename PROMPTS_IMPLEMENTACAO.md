# Roteiro de Implementação — Fatal Shooter (Prompts Organizados por Prioridade e Compatibilidade)

Este arquivo organiza os 4 prompts gerados na ordem ideal de execução, respeitando:
1. **Ordem estrita de prioridade do Documento Mestre (PDF):** Prioridade 1 (3Cs & Gunplay) antes de polimento visual/físico secundário.
2. **Dependências técnicas e prevenção de conflitos entre scripts:** evita retrabalho em `PlayerController.cs`, `Combat.cs`, `WeaponArt.cs` e `Fx.cs`.

---

## Ordem de Execução Recomendada

| Ordem | Tarefa | Arquivos Principais | Por que nesta ordem? |
| :--- | :--- | :--- | :--- |
| **1º** | **Recoil, Precisão (CoD/War Dogs), Muzzle Flash & Fumaça** | `Combat.cs`, `PlayerController.cs`, `Fx.cs` | É **Prioridade 1** do GDD (Gunplay). Prepara `WeaponDef` e `Fx.Flash` antes da troca de modelos 3D. |
| **2º** | **Exibição de Dano (`DamagePopups`) Discreta e Opcional** | `Settings.cs`, `Menus.cs`, `DamagePopups.cs` | Mudança isolada (zero conflito). Limpa a mira central imediatamente para testar o novo gunplay. |
| **3º** | **Modelos 3D e Texturas PBR Realistas (Mãos e Armas)** | `ViewHands.cs`, `WeaponArt.cs`, `SkinSurface.shader`, `AssetImport.cs`, Blender/Assets | Atualiza os viewmodels, UVs, texturas e pontos `Muzzle`/`Eject`/`Support`, conectando direto no kick e VFX já prontos do passo 1. |
| **4º** | **Física de Ragdoll Mobile-First na Morte** | `RagdollBody.cs`, `Actor.cs`, `AgentBody.cs`, `PlayerController.cs` | Toca em `PlayerController.cs` (câmera de morte) e `Actor.cs`/`Combat.cs` (direção do impacto) depois que o gunplay já estiver estabilizado. |

> [!TIP]
> Execute **um prompt por vez**, valide no Unity (compilação + cena `GreyboxCombatTest` ou `RobotFatalShooter`) e faça commit antes de passar para o próximo.

---

## 1º Prompt — Recoil, Precisão (CoD / War Dogs), Muzzle Flash e Fumaça no Cano

> **Prioridade:** 1 (GDD — 3Cs & Gunplay)  
> **Compatibilidade:** Base para o `Muzzle` dos novos modelos (3º prompt) e usa `Combat.cs` antes da integração de direção de impacto do Ragdoll (4º prompt).

```markdown
Atue como Engenheiro de Gunplay, Física de Câmera e VFX Mobile-First em Unity URP no repositório `robot-fatal-shooter-server` (`UnityProject/`).

## Objetivo
1. Reformular a mecânica de **recoil e precisão** (`Combat.cs` + `PlayerController.cs`) para entregar sensação tática realista estilo **War Dogs / Call of Duty**, com parâmetros organizados em `WeaponDef` para futura integração com o sistema de modificações do Menu Arsenal (Miras, Silenciadores, Empunhaduras).
2. Substituir o `Fx.Flash()` atual (esfera brilhante + `PointLight` por tiro) por um sistema realista e ultra-leve para mobile (60 FPS) de **Muzzle Flash em cross-quad/billboard aditivo** + **Fumaça no cano da arma** usando pooling.

## Alinhamento com o Documento Mestre (PDF)
- **Prioridade 1 (3Cs & Gunplay):** penalizar precisão ao atirar correndo/pulando, recompensando tiros parado, agachado, deitado (prone) ou em mira (ADS); TTK curto (3–4 tiros) exige controle real de recuo em rajadas.
- **Mobile-First (60 FPS em celulares intermediários):** zero alocação/destruição por bala (`Instantiate`/`Destroy` em rajadas), sem `PointLight` em tempo real por tiro no mobile, e contagem mínima de polígonos/overdraw nos efeitos.

## Estado Atual no Código
- `UnityProject/Assets/Scripts/Combat.cs`: `WeaponDef` define `spread`, `adsSpread`, `bloom`, `recoilPitch`, `recoilYaw`, `sprintToFire`.
- `UnityProject/Assets/Scripts/PlayerController.cs`:
  - `curSpread` soma `bloom * 0.45f` mesmo em ADS, tornando tiros em ADS imprecisos por cone aleatório em vez de mover a mira com o recoil.
  - `recoilOffset` e `recoilYawOffset` somam valores simples e sempre voltam 100% a `0` ao soltar o gatilho (`!firingActive`).
  - `vmKick` apenas empurra o viewmodel de forma linear.
- `UnityProject/Assets/Scripts/Fx.cs`: `Fx.Flash(Transform muzzle, Color c)` instancia uma `PrimitiveType.Sphere` + `Art.PointLight` a cada disparo, sem nenhum efeito de fumaça no cano.

## Requisitos de Implementação

### 1. Parâmetros Organizados em `WeaponDef` (`Combat.cs`)
Expanda e organize os campos de `WeaponDef` (sem criar ainda o sistema completo de inventário de attachments, mas deixando todos os parâmetros limpos e multiplicáveis para o futuro Menu Arsenal):
- **Precisão:** `hipSpreadBase`, `hipSpreadMovePenalty`, `hipSpreadAirPenalty`, `bloomPerShot`, `bloomMax`, `bloomDecay`, `adsSpreadBase` (próximo de `0°` para armas de precisão/fuzis; maior apenas em escopetas).
- **Recoil da Câmera:** `recoilPitchMin/Max`, `recoilYawBias` (tendência direcional sutil), `recoilYawJitter`, `firstShotKickMult` (primeiro tiro mais seco), `recoilRecoverySpeed`, `recoilSnapSpeed`.
- **Recoil Visual do Viewmodel (Gun Kick):** `vmKickBack`, `vmKickUp`, `vmKickRoll` (rotação lateral/pitch independente da câmera para dar peso mecânico estilo CoD).
- **VFX do Cano:** `muzzleFlashScale`, `muzzleSmokeAmount`, `muzzleFlashColor` (tons quentes realistas de pólvora/fogo: laranja-âmbar/amarelo-branco em vez de neon ciano/verde nas armas de fogo).

### 2. Mecânica de Recoil & Precisão Estilo CoD / War Dogs (`PlayerController.cs`)
- **ADS vs. Hipfire:**
  - **Em ADS (`ads == true`):** a bala vai **exatamente para onde a retícula/câmera aponta** (sem `bloom` aleatório invisível no cone, exceto dispersão natural de pellets na escopeta ou penalidade leve se estiver pulando/correndo). A dificuldade de manter o alvo em rajadas vem do **recoil físico na câmera + kick visual da arma**.
  - **Em Hipfire (`ads == false`):** mantenha cone dinâmico (`curSpread`) que abre com rajadas (`bloom`), movimento (`moveFrac`), sprint e ar (`!cc.isGrounded`), e fecha ao parar, agachar (`crouchK`) ou deitar (`proneK`).
- **Recuperação Híbrida Inteligente (Estilo CoD Modern Warfare):**
  - Separe o ângulo real da mira (`pitch`, `yaw`) do offset temporário de recoil (`recoilPitchCur`, `recoilYawCur`).
  - Ao soltar o gatilho, parte do `recoilPitchCur` recupera suavemente em direção ao ponto pré-rajada; **porém**, se o jogador puxar o mouse/polegar para baixo para controlar o spray manualmente durante ou logo após o tiro, abata o valor compensado do `recoilPitchCur` (cancelando o retorno automático na proporção compensada, para **nunca puxar a mira do jogador para baixo sozinho**).
- **Viewmodel Kick & Sway:**
  - Aplique `vmKickBack` (recuo para trás em Z), `vmKickUp` e `vmKickRoll` (leve torção) amortecidos por mola crítica (*spring damping*) no `vmRoot`, reduzidos em ADS para manter a visada limpa.

### 3. Muzzle Flash & Fumaça no Cano Mobile-First (`Fx.cs` + `PlayerController.cs`)
- **Muzzle Flash Realista em Cross-Quad (Pooled):**
  - Substitua a esfera + `PointLight` de `Fx.Flash()` por um **pool fixo** (ex.: 8–12 instâncias reutilizáveis) de um pequeno objeto leve contendo **2 planos cruzados em X/estrela + 1 billboard frontal** usando material **Aditivo** (`Particles/Standard Unlit` ou `Sprites/Default` aditivo) com textura procedural/compacta (64x64 ou 128x128) de **chama de disparo realista com núcleo branco-amarelado, línguas de fogo laranja e micro-faíscas**.
  - A cada tiro: posicione no `muzzle`, aplique **rotação aleatória no eixo Z** (`0..360°`), variação sutil de escala (`0.85..1.20 * w.muzzleFlashScale`) e vida ultracurta (`0.035s–0.055s`). Sem `PointLight` por bala no mobile (ou no máximo opcional apenas quando `Settings.gfx == 2`).
- **Fumaça Realista no Cano (Pooled):**
  - Adicione um pool leve de **puffs de fumaça** (quads billboard alpha-blended com a textura `puffTex` já existente ou versão suave em tons cinza-quente, `alpha` baixo ~`0.18–0.30`):
    - **Por disparo:** solte 2–3 puffs curtos que saem do cano com velocidade inicial frontal desacelerando rapidamente e subindo com leve turbulência (~`0.35s–0.6s`).
    - **Fumaça residual pós-rajada:** após disparos seguidos, mantenha um **fio leve de fumaça** subindo do cano por ~`0.8s–1.2s` (usando 1–2 partículas lentas do mesmo pool, respeitando `Fx.Scale` para aparelhos mais fracos).
  - Garanta `shadowCastingMode = Off` e `receiveShadows = false` em todos os renderers de flash e fumaça.

## Validação
- Compile no Unity e confirme zero erros/warnings.
- Teste em cena (ex.: `GreyboxCombatTest` ou `RobotFatalShooter`) com Fuzil (`VETOR`), Escopeta (`PULSO`) e Sniper (`SNIPER`):
  1. Em ADS parado, tiros únicos e rajadas curtas acertam exatamente onde a mira aponta, enquanto rajadas longas exigem controle de recuo; puxar o mouse/dedo para baixo cancela o retorno automático sem "puxão duplo".
  2. Em Hipfire correndo/pulando, a dispersão aumenta claramente e reduz ao agachar/deitar.
  3. O muzzle flash exibe chama em estrela realista sem bola colorida nem quedas de FPS, e a fumaça sai do cano naturalmente sem alocar objetos novos a cada tiro.
```

---

## 2º Prompt — Exibição de Dano (`DamagePopups`) Discreta e Desativada por Padrão

> **Prioridade:** 2 (HUD Limpo Mobile-First / Feedback de Combate)  
> **Compatibilidade:** Totalmente isolado (`Settings.cs`, `Menus.cs`, `DamagePopups.cs`). Não conflita com nenhum outro prompt.

```markdown
Atue como Engenheiro de UI/HUD e Gameplay em Unity no repositório `robot-fatal-shooter-server` (`UnityProject/`).

## Objetivo
Modificar a mecânica de exibição de números de dano (`DamagePopups.cs`) para torná-la **desativada por padrão** nas configurações (mantendo o HUD mobile limpo conforme o Documento Mestre) e, quando ativada pelo jogador, exibir números **menores, mais discretos (menor opacidade) e que nunca tampem a mira central**.

## Estado Atual no Código
- `UnityProject/Assets/Scripts/Settings.cs`: armazena as opções do jogador via `PlayerPrefs` (chaves `na_*`) em `Load()` e `Save()`, mas não possui opção para números de dano.
- `UnityProject/Assets/Scripts/Menus.cs`: exibe a tela de Configurações/Controles onde o jogador altera opções do jogo.
- `UnityProject/Assets/Scripts/DamagePopups.cs`:
  - `Show(Vector3 worldPos, float value, int region)` e `Self(float value, string weapon)` estão sempre ativos.
  - Atualmente usa fontes grandes (`34f`, `38f`, `44f` * `1.45f` no pico = até ~64px), `alpha = 1f` até 65% da vida útil, e projeta via `cam.WorldToScreenPoint(wp)` com jitter pequeno (`-28f..28f`), fazendo os números caírem exatamente em cima da mira central `(960, 540)` na resolução virtual `1920x1080`.

## Requisitos de Implementação

### 1. Nova Configuração em `Settings.cs` e `Menus.cs`
- Adicione em `Settings.cs`:
  - `public static bool showDamage = false;` (**padrão desativado**).
  - Persistência em `Load()` e `Save()` usando `PlayerPrefs.GetInt("na_dmgpop", 0) == 1` / `PlayerPrefs.SetInt("na_dmgpop", showDamage ? 1 : 0)`.
- Adicione o toggle **"EXIBIR DANO"** (Ligado / Desligado, padrão Desligado) no menu de configurações em `Menus.cs` (junto às opções de HUD/Mira), salvando automaticamente via `Settings.Save()`.

### 2. Comportamento quando Desativado (`Settings.showDamage == false`)
- Ocultar **todos** os números de dano (`DamagePopups.Show` e `DamagePopups.Self` retornam imediatamente sem adicionar à lista, e `OnGUI()` limpa/não desenha popups).
- **Manter intacto** o hitmarker tático no centro da mira (branco no tronco, vermelho na cabeça, conforme GDD).

### 3. Visual e Posicionamento quando Ativado (`Settings.showDamage == true`)
- **Menor tamanho:** reduza o tamanho base da fonte em `DamagePopups.cs` para uma faixa discreta (ex.: **18px corpo, 21px peito, 24px cabeça**, com escala inicial `pop` suave de no máximo `1.15f`).
- **Mais discreto (menor opacidade):** aplique `alpha` máximo entre **0.65 e 0.75** (em vez de `1.0`), com contorno/sombra proporcionalmente mais leve (`c.a * 0.6f` e deslocamento de `1f` em vez de `2f`) para não poluir a visão em telas pequenas de celular.
- **Nunca tampar a mira (Zona Morta Central):**
  - Na resolução virtual (`1080f` de altura, centro da tela `cx = (Screen.width / s) * 0.5f`, `cy = 540f`), defina uma **zona morta circular** ao redor da mira central (ex.: raio mínimo de **80–95px**).
  - Ao calcular a posição na tela `(x, y)` de um `Pop`, se a distância até `(cx, cy)` for menor que o raio da zona morta, empurre o número radialmente ou diagonalmente para fora (preferindo o quadrante lateral/superior direito ou alternando lado esquerdo/direito em rajadas rápidas) mantendo-o próximo do inimigo atingido, sem nunca invadir a retícula.
- **Rajadas rápidas sem empilhamento:**
  - Mantenha números individuais por acerto, mas aplique leve escalonamento/alternância de offset horizontal e vertical para que múltiplos tiros seguidos (ex.: fuzil/SMG) fiquem legíveis sem se sobrepor nem invadir o centro da mira.

## Validação
- Compile no Unity e confirme ausência de erros/warnings.
- Teste em jogo:
  1. Com configuração padrão (primeira execução ou após reset), atirar em inimigos e sofrer auto-dano **não** exibe números de dano, mantendo apenas o hitmarker.
  2. Ao ativar a opção no menu, reiniciar a partida/sessão e confirmar que a preferência foi salva.
  3. Atirar em inimigos em curta e longa distância (Hipfire e ADS) e verificar que os números aparecem menores, translúcidos (`alpha ~0.65–0.75`) e **sempre fora da área da mira**.
```

---

## 3º Prompt — Modelos 3D e Texturas PBR Realistas de Mãos e Armas

> **Prioridade:** 3 (Qualidade Visual Mobile-First — Viewmodel)  
> **Compatibilidade:** Atualiza `WeaponArt.Muzzle`, `WeaponArt.Support`, `WeaponArt.Eject`, `ViewHands.cs` e `SkinSurface.shader`, acoplando nos sistemas de recoil/kick e Muzzle Flash/Fumaça já prontos do **1º Prompt**.

```markdown
Atue como Engenheiro Técnico de Arte 3D, Shaders e Unity URP Mobile-First no repositório `robot-fatal-shooter-server` (`UnityProject/`).

## Objetivo
Substituir os modelos e texturas atuais de **mãos/antebraços em primeira pessoa** e das **4 armas principais de fogo** (`Rifle` / Fuzil de Assalto, `SMG` / Carabina Compacta ou slot equivalente, `Shotgun` / Escopeta tática, e `Rail` / Rifle de Precisão) por **modelos low-poly realistas com UVs limpas** e **texturas PBR realistas** otimizadas para rodar a **60 FPS estáveis em celulares intermediários** (WebGL Mobile e Android/iOS em Unity URP).
Deixe `Launcher` (Bazuca) e `Plasma` intactos para uma etapa futura.

## Diretrizes Obrigatórias do GDD (Mobile-First & Guerra Urbana Moderna)
1. **Estética "pé-no-chão" (Guerra Urbana Contemporânea):**
   - Armas de fogo reais/táticas com materiais críveis: aço escovado/fosfatizado com leve desgaste nas arestas, polímero fosco texturizado, trilhos Picatinny, borracha no punho e vidro de mira transparente/antirreflexo. Evite visual neon/arcade ou alto contraste exagerado.
   - Mãos e antebraços em primeira pessoa com **luvas táticas meio-dedo**: dedos expostos com textura realista de pele humana (poros, dobras das falanges, unhas sutis), palma e dorso da luva em couro/kevlar com costura e proteção rígida nos nós dos dedos, e manga de uniforme militar fosco com patch identificando a cor do time (`Art.TeamColor(team)`).
2. **Orçamento Mobile-First (60 FPS em aparelhos intermediários):**
   - **Polígonos:** máximo ~3.000–5.000 tris por arma em primeira pessoa; máximo ~2.500–4.000 tris para o conjunto de mãos + antebraços.
   - **Draw Calls / Materiais:** **1 único atlas PBR por arma** (exceto vidro da mira `lens` e ponto emissivo `glow` se necessário) e **1 único atlas PBR para mãos/luvas/manga** (com máscara ou slot simples para o patch do time).
   - **Texturas:** resolução máxima **1024x1024** para armas/mãos em PC/Android e **512x512 ou 1024x1024 comprimido** (ETC2/ASTC/WebGL Automatic) em Mobile/WebGL, com mipmaps e anisotropia controlada (4x).
   - **Iluminação e Shaders:** compatível com iluminação *baked* e FOV mobile de **60° a 65°** (alinhamento limpo de mira/ADS sem obstruir a tela). Sem *reflection probes* pesadas em tempo real ou shaders multi-pass custosos.

## Estado Atual do Repositório (Arquivos-Chave)
- `UnityProject/Assets/Scripts/ViewHands.cs`: hoje tenta carregar `Resources/Hands/ArmL` (pasta `Assets/Resources/Hands/` está vazia) e cai em primitivas (`Sphere`, `Capsule`, `Box`, `Tube`) com `Art.Mat(...)` sem textura. Preserva a lógica de animação de recarga que procura filhos nomeados `"Mag"` ou `"Pump"`, e os offsets `GripP/GripR` e `SupP/SupR`.
- `UnityProject/Assets/Scripts/WeaponArt.cs`: carrega modelos de `Assets/Resources/Weapons/` (`Rifle`, `Shotgun`, `Rail`, etc.) e aplica `Art.PbrLocal(...)` (triplanar via `NeonSurface.shader`) ou `Skins.MaterialFor(...)`.
- `UnityProject/Assets/Resources/SkinSurface.shader`: shader URP por UV (`_BaseMap`, `_NormalMap`, `_MaskMap` onde `R = Metallic`, `G = Roughness`, `B = Emissive`).
- `UnityProject/Assets/Scripts/Skins.cs`: gerencia skins por arma e `AtlasMaterial(...)`.
- `UnityProject/Assets/Editor/AssetImport.cs`: regras de importação (`AssetPostprocessor`) que atualmente só tratam `/Resources/Tex/`, `/Resources/Agents/` e `/Resources/Sky/`.

## Tarefas de Execução (Passo a Passo)

### 1. Modelos 3D e UVs (Blender MCP / Scripts de Geração `.fbx`/`.glb`)
- Crie ou importe e otimize no Blender:
  - **Mãos e Antebraços (`Assets/Resources/Hands/`):** modelo realista de mão direita (`HandR` / `ArmR`), mão esquerda de apoio (`ArmL`) e manga/antebraço com luva meio-dedo, topologia limpa para primeira pessoa, UVs sem sobreposição (non-overlapping) empacotadas em 1 atlas.
  - **4 Armas Principais (`Assets/Resources/Weapons/`):** `Rifle.fbx` (Fuzil de Assalto tático), `Shotgun.fbx` (Escopeta tática pump/semi), `Rail.fbx` (Rifle de Precisão de ferrolho com luneta) e uma variante/modelo de Carabina/SMG compatível com o slot atual.
  - **Hierarquia obrigatória:** garanta que carregadores removíveis tenham o objeto filho exatamente nomeado `"Mag"` (e `"Pump"` na escopeta) para manter a animação de recarga do `ViewHands.cs` funcionando, além de alinhar os pontos `Muzzle`, `Support` e `Eject` no `WeaponArt.cs`.

### 2. Texturas PBR Realistas (Bake / Geração Procedural ou Fotográfica em Atlas)
- Gere os mapas PBR empacotados no padrão esperado pelo `SkinSurface.shader` (ou variante mobile otimizada):
  - `*_a.png` / `.jpg` (Albedo em sRGB, tons militares realistas e pele natural).
  - `*_n.png` / `.jpg` (Normal Map OpenGL em espaço Linear: granulação de polímero, metal escovado, costura de couro, poros/dobras da pele).
  - `*_m.png` / `.jpg` (Mask Map Linear: `R = Metallic`, `G = Roughness/AO`, `B = Emissive` apenas onde aplicável).
- Garanta resolução 1024x1024 (com perfil WebGL/Mobile respeitando 512–1024px).

### 3. Shader e Integração no Unity (`SkinSurface.shader`, `AssetImport.cs`, `WeaponArt.cs`, `ViewHands.cs`)
- **Shader (`SkinSurface.shader`):** revise e otimize para URP Mobile (`#pragma target 3.0` se viável para WebGL2/GLES3, mantendo suporte a `_BaseMap`, `_NormalMap`, `_MaskMap` e tint/patch do time sem branches pesados).
- **Importação (`AssetImport.cs`):** adicione regras em `OnPreprocessTexture()` e `OnPreprocessModel()` para `/Resources/Weapons/` e `/Resources/Hands/`:
  - `_n` como `NormalMap` (sRGB = false), `_m` em espaço Linear (`sRGBTexture = false`), `_a` em sRGB (`sRGBTexture = true`).
  - Compressão mobile/WebGL (`maxTextureSize = 1024` ou `512` em WebGL, `mipmapEnabled = true`, `anisoLevel = 4`).
  - Modelos sem importação de câmeras/luzes/blendshapes desnecessários (`Read/Write` desativado se não usado em runtime).
- **`WeaponArt.cs`:** atualize o carregamento padrão das 4 armas principais para usar os novos materiais PBR com mapas UV dedicados via `SkinSurface.shader` (mantendo `lens` transparente e compatibilidade total com `Skins.MaterialFor(...)`). Ajuste `Muzzle`, `Support` e `Eject` para as dimensões reais dos novos modelos.
- **`ViewHands.cs`:** substitua o fallback de primitivas por carregamento dos novos assets em `Resources/Hands/` com o atlas PBR realista (pele + luva meio-dedo + manga + patch com cor do time), mantendo `shadowCastingMode = Off`, offsets de `GripP/GripR`, `SupP/SupR` e animação de `Mag`/`Pump` alinhados para FOV 60°–65°.

### 4. Validação
- Verifique no Unity (via `unity-mcp` ou compilação/logs) que não há erros de compilação C# nem warnings de shader no URP.
- Valide em cena de teste (ou Greybox) com FOV 60°–65° que:
  1. As mãos seguram corretamente o punho e o guarda-mão sem *clipping* grosseiro e a recarga move `"Mag"`/`"Pump"` corretamente.
  2. Os materiais PBR exibem especularidade realista em metal/couro/pele sob iluminação *baked*.
  3. O orçamento de triângulos, draw calls e memória de textura permanece dentro do alvo mobile-first para 60 FPS.
```

---

## 4º Prompt — Física de Ragdoll Mobile-First na Morte

> **Prioridade:** 4 (Feedback Físico & Imersão)  
> **Compatibilidade:** Executado por último para que `PlayerController.cs` (câmera/viewmodel) e `Combat.cs` já estejam finalizados com o novo gunplay e viewmodel.

```markdown
Atue como Engenheiro de Física e Gameplay em Unity URP Mobile-First no repositório `robot-fatal-shooter-server` (`UnityProject/`).

## Objetivo
Substituir a animação atual de morte (rotação rígida de `-86°` do modelo inteiro em `AgentBody.Update()`) por um sistema de **Ragdoll com física realista e otimizada para mobile (60 FPS em celulares intermediários)** para **bots, jogadores remotos e jogador local** (com câmera de morte em 3ª pessoa para o jogador local).

## Contexto Atual no Código
- `UnityProject/Assets/Scripts/Actor.cs`:
  - `Die(Actor from, bool head, string weapon)` e `SetNetAlive(bool isAlive)` chamam `body.Die()` e desativam o `CharacterController` (`cc.enabled = false`).
  - `Respawn(Vector3 pos, float yaw)` ocorre em ~3–4s (`respawnAt`) e reutiliza o mesmo `Actor` e `AgentBody` via `body.Revive()`.
  - Quando `isPlayer == true`, `Actor.Init()` não constrói `AgentBody` (o jogador local usa apenas `ViewHands` em 1ª pessoa).
- `UnityProject/Assets/Scripts/AgentBody.cs`:
  - Suporta dois rigs: GLBs em `Resources/Agents/GLB/<file>` (ossos `Hips`, `Spine02`/`Spine01`/`Spine`, `Head`, `LeftUpLeg`/`RightUpLeg`, `LeftLeg`/`RightLeg`, `LeftArm`/`RightArm`, `LeftForeArm`/`RightForeArm`) e FBXs Humanoid em `Resources/Agents/<file>` (via `Animator.GetBoneTransform`).
  - Atualmente `Die()` apenas oculta `gun` e gira `model.transform.localRotation` por `0.55s`.
- `UnityProject/Assets/Scripts/PlayerController.cs`:
  - Controla a câmera de 1ª pessoa e `OnRespawn()` do jogador local.

## Requisitos de Design e Performance Mobile-First (GDD 5v5 / 60 FPS)
1. **Pool / Clone Visual Desacoplado:**
   - Ao morrer, **não** transforme o `AgentBody` vivo em ragdoll diretamente. Oculte o modelo vivo (`model.SetActive(false)` / `gun.SetActive(false)`) e instancie ou reutilize de um **Pool de Ragdolls** um clone visual com a aparência/time do agente (`AgentDef` + `team`), copiando a pose exata dos ossos no momento da morte.
   - Isso garante que o `Actor` renasça limpo em `Respawn()`/`Revive()` sem fazer o cadáver sumir antes da hora.
   - O cadáver permanece no chão por **~5 a 6 segundos** e depois retorna ao pool (ou é destruído com segurança).
2. **Jogador Local (Ragdoll + Câmera de Morte em 3ª Pessoa):**
   - Quando `isPlayer == true` morrer, oculte o viewmodel (`ViewHands` / arma em 1ª pessoa), gere o ragdoll em 3ª pessoa correspondente ao `AgentDef` e `team` do jogador na posição/rotação atuais, e ative uma **câmera de morte em 3ª pessoa** suave (afastada ~2,5–3,5m acompanhando o `Hips`/tronco do ragdoll, com colisão simples de câmera para não atravessar paredes) até o `OnRespawn()`.
3. **Orçamento Físico Mobile-First (10–11 Ossos):**
   - Monte o ragdoll programaticamente (ou via componente `RagdollBody.cs`) com no máximo **10–11 `Rigidbody` + `CapsuleCollider`/`BoxCollider` + `CharacterJoint`**:
     - `Hips` (raiz)
     - `Spine` / `Chest` (1 segmento de tronco)
     - `Head`
     - `LeftArm`, `LeftForeArm`, `RightArm`, `RightForeArm`
     - `LeftUpLeg`, `LeftLeg`, `RightUpLeg`, `RightLeg`
     - (Omitir mãos, pés, dedos e ombros para economizar processamento).
   - Configure `CharacterJoint` com limites angulares anatômicos (`lowTwistLimit`, `highTwistLimit`, `swing1Limit`, `swing2Limit`), `enableProjection = true` (evita esticamento de ossos) e `enableCollision = false` (sem auto-colisão entre partes do mesmo corpo).
   - **Camada de Física Isolada (`Ragdoll`):** configure `Physics.IgnoreLayerCollision` para que ragdolls colidam **apenas com a geometria estática do cenário** (não bloqueiam tiros/raycasts, `CharacterController` de jogadores vivos ou outros ragdolls).
   - **Limite Simultâneo e Auto-Sleep:** mantenha no máximo **4 a 5 ragdolls ativos simultaneamente** (reciclando o mais antigo se exceder). Após o ragdoll estabilizar (velocidade baixa por ~0,5s ou após ~3s), coloque os `Rigidbody` em `Sleep()` ou `isKinematic = true` para zerar o custo de CPU até o despawn.
4. **Impulso Direcional e Inércia:**
   - Passe para o spawner de ragdoll: `actor.velocity` (inércia do movimento), direção do tiro (`hitDir`), `head` (booleano de headshot) e `weapon` (tipo de arma).
   - Aplique `ForceMode.Impulse` proporcional:
     - `head == true`: impulso concentrado em `Head` e `Spine` jogando para trás.
     - Escopeta / explosões / Sniper: impulso mais forte no tronco (`Spine`/`Hips`).
     - Demais armas: impulso moderado na direção do impacto + leve componente vertical para queda natural.
5. **Arma Oculta na Morte:**
   - Mantenha a arma do personagem morto oculta (`gun.SetActive(false)`), sem criar `Rigidbody` extra para a arma.

## Tarefas de Implementação
1. **Criar `UnityProject/Assets/Scripts/RagdollBody.cs` (ou equivalente):**
   - Responsável por construir os 10–11 ossos (suportando tanto a hierarquia dos GLBs quanto `Animator.GetBoneTransform` dos FBXs), configurar massa distribuída realista (~70kg total), `CharacterJoint` com limites, camada `Ragdoll`, cópia de pose bone-a-bone da instância viva, aplicação do impulso direcional, auto-sleep e pool com limite máximo de 4–5 ragdolls ativos.
2. **Atualizar `UnityProject/Assets/Scripts/Actor.cs` e `AgentBody.cs`:**
   - Guardar ou repassar o último vetor de impacto (`lastHitDir`, `lastHitHead`, `lastHitWeapon`) em `TakeDamage` / `Die` / `SetNetAlive`.
   - Em `AgentBody.Die()`, ocultar `model` e `gun` e acionar o `RagdollBody` (removendo a rotação artificial `-86f * k` de `Update()`).
   - Em `AgentBody.Revive()`, garantir que `model` volte limpo (`localRotation = Quaternion.identity`, `SetActive(true)`).
3. **Atualizar `UnityProject/Assets/Scripts/PlayerController.cs`:**
   - Ao morrer o jogador local, spawnar o ragdoll do seu agente e transicionar a câmera para 3ª pessoa focando o ragdoll até `OnRespawn()`, restaurando a câmera e o `ViewHands` ao renascer.
4. **Configuração de Layer / Collision Matrix (Editor ou Runtime):**
   - Garantir que a layer de Ragdoll ignore tiros/raycasts (`Combat.cs` / `Bullet.cs`) e `CharacterController` de vivos, colidindo apenas com paredes/pisos do mapa (alinhado aos `BoxCollider`/`CapsuleCollider` primitivos descritos no PDF).

## Validação
- Compile e verifique no Unity (`unity-mcp` ou logs de compilação) que não há erros nem warnings.
- Teste em cena (ex.: `GreyboxCombatTest` ou `RobotFatalShooter`) matando bots com tiro no corpo, headshot e escopeta, e matando o jogador local:
  1. O ragdoll herda a pose e velocidade do momento da morte e reage na direção do tiro sem esticar ossos nem atravessar o chão.
  2. Cadáveres não bloqueiam tiros nem o movimento de jogadores vivos.
  3. O respawn em 3–4s ocorre sem bugs visuais e a câmera do jogador local retorna perfeitamente para 1ª pessoa.
```
