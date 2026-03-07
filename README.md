# Burst Strike

## Table of Contents

### English
- Project Overview
- Repository Structure
- Vision
- Architecture Plan
- Current Stage
- Current Client Focus
- Design Principles
- Technical Foundations Already Visible
- Roadmap
- Summary

### 中文
- 项目定位
- 仓库目录结构
- 设计愿景
- 整体架构规划
- 当前阶段
- 当前客户端重点
- 设计原则
- 当前已可见的技术基础
- 演进方向
- 总结

---

## English

### Project Overview

`Burst Strike` is an RTS framework designed around three long-term goals: **cross-platform support**, **high performance**, and **high extensibility**.

The project is currently in an **initialization / architecture validation stage**. At this stage, the priority is not rapid gameplay production, but building a strong technical foundation for long-term RTS development.

The framework is planned to evolve around three major parts:

- a Unity-based client runtime
- a lockstep-oriented battle server built with .NET Socket
- a .NET-based store and payment service

The goal is not to create a short-term demo, but to establish a practical framework that can support medium-to-large RTS projects over time.

### Repository Structure

The repository is organized as a Unity project with the gameplay framework concentrated under `Assets/Game/`.

```text
Burst Strike/
├─ Assets/
│  ├─ Game/
│  │  ├─ Core/                # shared foundations (deterministic fixed-point math, common primitives)
│  │  │  └─ Fixed/
│  │  ├─ Spatial/             # spatial layer: grid, map, and pathfinding
│  │  │  ├─ Grid/
│  │  │  ├─ Map/
│  │  │  └─ Pathing/
│  │  ├─ Gameplay/            # gameplay-layer modules
│  │  │  ├─ Commands/          # command encoding/decoding and scheduling
│  │  │  └─ Units/             # unit system (actors, abilities, activities, factory, bullets)
│  │  ├─ Data/                # data-driven layer
│  │  │  └─ Units/             # YAML/unit data loading + validation + samples
│  │  ├─ Docs/                # project-side design notes and supporting documents
│  │  └─ World/               # world orchestration, logic threading, rendering snapshots
│  └─ Scenes/
│     └─ SampleScene.unity    # current prototype/testing scene
├─ Packages/
│  ├─ manifest.json           # Unity package dependencies
│  └─ packages-lock.json      # Unity package lock file
├─ ProjectSettings/           # Unity project configuration
└─ README.md
```

#### Directory Notes

- `Assets/Game/Core/Fixed/`  
  Deterministic fixed-point math foundation used by the simulation core.

- `Assets/Game/Spatial/`  
  The spatial layer of the framework: `Grid/`, `Map/`, and `Pathing/`.

- `Assets/Game/Gameplay/Commands/`  
  Command construction, encoding/decoding, and scheduling; an important base for future lockstep input flow.

- `Assets/Game/Data/Units/`  
  YAML-driven unit archetype loading, validation, editor helpers, tests, and sample YAML assets (e.g. `Data/Units/Samples`).

- `Assets/Game/Gameplay/Units/`  
  Unit-centric gameplay abstractions (actors, abilities, activities, unit factory, bullets).

- `Assets/Game/World/`  
  Contains the world-level orchestration layer. It is structured around clear responsibilities:
  - `UnityWorld/`: Unity-facing bridge and MonoBehaviour-side world control
  - `Logic/`: background logic world and simulation-side logic
  - `Rendering/`: render snapshots and render-side bridge types
  - `Services/`: world runtime services such as occupancy and enemy search
  - `Config/`: world config models and YAML/config loading helpers
  - `Core/`: core contracts and registries

### Vision

#### Cross-Platform

The client is built with Unity and is intended to run across multiple platforms. To support this, the architecture aims to keep the following concerns decoupled:

- Rendering
- Simulation
- Networking
- Data-driven configuration

This helps reduce coupling between core logic and engine/platform-specific presentation code.

#### High Performance

RTS games naturally face heavy computational pressure from large unit counts, pathfinding, behavior scheduling, and synchronization. Performance is therefore treated as a first-class architectural concern.

Key directions include:

- separating simulation from Unity presentation
- splitting responsibilities between the Unity main thread and a dedicated logic thread
- avoiding Unity API usage in the logic core whenever possible
- using queues and snapshots for thread communication
- leaving room for large-scale simulation and server growth

#### High Extensibility

The framework is meant to grow in a controlled way rather than become harder to maintain as features increase.

Planned areas of extensibility include:

- Unit system
- Behavior system
- Ability system
- Navigation and pathfinding
- Lockstep synchronization
- Battle server
- Store and payment services

### Architecture Plan

The overall project is planned around **three major modules**.

#### Architecture at a Glance

- 🎮 **Gameplay Client Framework**  
  Runs inside Unity and serves as the playable runtime foundation. It is responsible for presentation, local simulation hosting, command intake, map/pathfinding integration, and the bridge between main-thread visuals and logic-thread simulation.

- 🌐 **.NET Socket-Based Battle Server**  
  Acts as the future lockstep synchronization coordinator. It is responsible for room management, frame progression, input distribution, connection state handling, and multiplayer battle order.

- 💳 **.NET Store and Payment Service**  
  Serves as the peripheral business backend. It is responsible for product management, order handling, payment channel integration, callback processing, and account-side asset updates.

#### 1. Gameplay Client Framework

This is the part that already has an initial implementation in the current repository. It is responsible for:

- runtime execution
- RTS core simulation
- unit state progression
- command dispatch
- map and pathfinding
- presentation of simulation results

This module follows a **lockstep-oriented design philosophy** and is internally split into two major threads.

##### Unity Main Thread

Main responsibilities:

- scene lifecycle management
- GameObject and rendering object management
- input collection
- debug visualization
- visual presentation of simulation results

The Unity main thread is primarily the **presentation layer**. It should not carry the heavy simulation workload.

##### Unity Logic Thread

Main responsibilities:

- fixed-step logic ticks
- unit state progression
- behavior and activity scheduling
- command execution
- pure logic computation such as navigation, pathfinding, and occupancy
- snapshot output for the main thread

The logic thread is designed to remain as a **portable, controllable, and extensible simulation core**, with minimal Unity API dependency.

##### Current Repository Mapping

The current codebase already reflects this direction:

- `Assets/Game/World/UnityWorld/World.cs`  
  Unity-side bridge for logic thread management, command exchange, snapshot consumption, and presentation driving.

- `Assets/Game/World/Logic/LogicWorld.cs`  
  Core simulation world running on the dedicated logic thread and advancing in ticks.

- `Assets/Game/Gameplay/Commands/`  
  Command construction, encoding, and scheduling; an important base for future lockstep input flow.

- `Assets/Game/Spatial/Map/` and `Assets/Game/Spatial/Pathing/`  
  Core RTS map and navigation capabilities.

- `Assets/Game/Gameplay/Units/`  
  Foundational object models for units, behaviors, abilities, and activities.

In short, the repository is still early, but the foundation for **presentation / simulation separation** is already visible.

#### 2. .NET Socket-Based Battle Server

The second planned module is a **lockstep battle server**.

Its job is not rendering, but managing synchronization order in multiplayer battles, including:

- room management
- player connection and disconnection handling
- frame progression
- input collection and broadcast
- match state maintenance
- timeout handling and basic anti-cheat

In a lockstep model, the server acts as the **synchronization coordinator**, ensuring that all clients receive the same input on the same timeline.

This service is planned to be built with .NET Socket in order to keep the communication layer lightweight, stable, and easy to extend toward room, battle, replay, and spectating services.

#### 3. .NET-Based Store and Payment Service

The third planned module is a **store and payment service layer**.

Its scope includes:

- product management
- order management
- payment channel integration
- payment callback processing
- account asset updates
- store-related APIs

Unlike the battle server, this service does not participate in real-time game synchronization. Instead, it supports business and live-operation workflows.

Separating battle synchronization from store/payment responsibilities helps keep the architecture clean, secure, and easier to scale independently.

### Current Stage

The project should currently be understood as:

- an RTS framework prototype
- a testbed for thread separation and logic layering
- the starting point of a future client + battle server + store server ecosystem

It is **not** yet a fully content-complete game project.

The current stage is mainly validating:

- whether main-thread / logic-thread separation is stable
- whether the simulation core can gradually decouple from Unity presentation
- whether command-driven ticking is suitable for lockstep
- whether map, unit, behavior, and pathfinding systems can evolve cleanly
- whether the architecture can naturally grow toward a .NET battle server

### Current Client Focus

#### 1. World-Driven Architecture

The split between `World` and `LogicWorld` establishes:

- a main-thread world
- a logic-thread world
- inter-thread communication
- snapshot-based output

This is the foundation of the RTS framework.

#### 2. Command-Driven Simulation

The command model and encoding structure prepare the project for a unified input pipeline. Inputs from local players, AI, network frames, or test scripts should all eventually become one consistent command stream entering the logic world.

#### 3. Unit and Behavior Abstraction

RTS is fundamentally about large numbers of units operating under the same rules: moving, patrolling, guarding, attacking, navigating, and switching states. That is why units, abilities, activities, and behaviors are a central long-term concern of the framework.

#### 4. Data-Driven Design

The repository already contains YAML-related structures. Over time, unit stats, world parameters, behavior tuning, and ability data should become increasingly data-driven to reduce hardcoded logic and improve iteration efficiency.

### Design Principles

#### 1. Separate Presentation from Simulation

The presentation layer answers **what should be shown**, while the simulation layer answers **what is actually happening**. They should communicate through snapshots, events, and commands rather than direct coupling.

#### 2. Define Boundaries Before Expanding Gameplay

At this stage, architectural boundaries matter more than rapidly adding gameplay content. In RTS projects, unclear boundaries become increasingly expensive as the system grows.

#### 3. Let Lockstep Constraints Shape the System

Because lockstep synchronization is a target, the project must be constrained early by tick rules, input structure, state progression, determinism, and clear client/server responsibilities.

#### 4. Keep the Simulation Core Portable

Even if logic currently runs in a client-side thread, the simulation core should remain as portable as possible, so that parts of it can evolve naturally toward standalone server-side services later.

### Technical Foundations Already Visible

The repository already shows several foundational pieces:

- dual-thread structure with `World` and `LogicWorld`
- command factory and command encoding support
- map, pathfinding, and debug utilities
- units, activities, and abilities as core object models
- YAML loading and sample configuration assets

These do not mean the system is complete. They mainly serve as validation for architecture boundaries, threading, and data organization.

### Roadmap

Planned future work includes:

- more complete unit lifecycle management
- more stable command queuing and frame execution
- client input collection and synchronization integration
- a .NET Socket lockstep server prototype
- room / match / frame broadcast mechanisms
- replay, spectating, and log verification support
- store service, order service, and payment integration
- more standardized data configuration and hot-update support

### Summary

`Burst Strike` is not intended to be just another Unity RTS demo. Its long-term purpose is to grow into a practical RTS framework with a clear separation of responsibilities:

- the client handles presentation and local simulation hosting
- the battle server handles lockstep synchronization and match orchestration
- the store server handles payment and operation-side business

The current repository already contains the earliest technical layer of that vision: a Unity client foundation with main-thread / logic-thread separation and the early shape of RTS core modules.

Future development will continue around the goals of **cross-platform**, **high performance**, and **high extensibility**.

---

## 中文

### 项目定位

`Burst Strike` 是一个以 **跨平台**、**高性能**、**高扩展** 为长期目标的 RTS 游戏框架。

当前项目仍处于 **初始化 / 架构验证阶段**。现阶段重点不是快速堆叠玩法，而是优先建立适合 RTS 长期演进的底层技术体系。

该框架未来将围绕三大部分逐步演进：

- 基于 Unity 的客户端运行时
- 基于 .NET Socket 的帧同步战斗服务器
- 基于 .NET 的商城与支付服务

目标不是做一个短期 Demo，而是逐步打磨一套适合中大型 RTS 项目持续演进的基础框架。

### 仓库目录结构

当前仓库是一个 Unity 项目，核心玩法框架主要集中在 `Assets/Game/` 下。

```text
Burst Strike/
├─ Assets/
│  ├─ Game/
│  │  ├─ Core/                # 通用基础能力（确定性定点数学、共享基础类型）
│  │  │  └─ Fixed/
│  │  ├─ Spatial/             # 空间层：Grid/Map/Pathing
│  │  │  ├─ Grid/
│  │  │  ├─ Map/
│  │  │  └─ Pathing/
│  │  ├─ Gameplay/            # 玩法层模块
│  │  │  ├─ Commands/          # 命令编解码与调度
│  │  │  └─ Units/             # 单位系统（Actor/Ability/Activity/Factory/Bullet 等）
│  │  ├─ Data/                # 数据驱动层
│  │  │  └─ Units/             # 单位 YAML 数据加载/校验/样例
│  │  ├─ Docs/                # 项目设计说明与辅助文档
│  │  └─ World/               # 世界调度、逻辑线程与渲染快照桥接
│  └─ Scenes/
│     └─ SampleScene.unity    # 当前原型/测试场景
├─ Packages/
│  ├─ manifest.json           # Unity 包依赖配置
│  └─ packages-lock.json      # Unity 包锁文件
├─ ProjectSettings/           # Unity 工程配置
└─ README.md
```

#### 目录说明

- `Assets/Game/Core/Fixed/`  
  确定性定点数学基础，用于模拟核心。

- `Assets/Game/Spatial/`  
  空间层：`Grid/`、`Map/` 与 `Pathing/`（网格、地图与寻路/导航）。

- `Assets/Game/Gameplay/Commands/`  
  命令构造、编解码与调度，是后续帧同步输入流的重要基础。

- `Assets/Game/Data/Units/`  
  基于 YAML 的单位 archetype 数据加载、校验、编辑器辅助、测试与样例 YAML（例如 `Data/Units/Samples`）。

- `Assets/Game/Gameplay/Units/`  
  单位系统（Actor/Ability/Activity/Factory/Bullet 等）的核心抽象。

- `Assets/Game/World/`  
  世界级调度层，并已按职责拆分为：
  - `UnityWorld/`：Unity 侧桥接与 MonoBehaviour 世界控制
  - `Logic/`：后台逻辑世界与模拟逻辑
  - `Rendering/`：渲染快照与渲染桥接类型
  - `Services/`：占位、敌人搜索等世界运行时服务
  - `Config/`：世界配置模型与 YAML/配置加载辅助
  - `Core/`：核心契约与注册表

### 设计愿景

#### 跨平台

客户端基于 Unity 构建，并以多平台支持为目标。为此，架构上尽量解耦以下部分：

- 表现层
- 游戏逻辑
- 网络通信
- 数据驱动配置

这有助于降低核心逻辑与引擎或平台表现层之间的耦合。

#### 高性能

RTS 天然面临大量单位模拟、寻路、行为调度与同步计算带来的性能压力，因此性能被视为一等公民。

核心方向包括：

- 将逻辑模拟与 Unity 表现层分离
- 区分 Unity 主线程与独立逻辑线程的职责
- 逻辑核心尽量避免依赖 Unity API
- 通过队列与快照进行线程通信
- 为大规模模拟与服务端扩展预留空间

#### 高扩展

框架设计目标是可持续扩展，而不是随着功能增加快速失控。

可扩展方向包括：

- 单位系统
- 行为系统
- 技能系统
- 导航与寻路
- 帧同步
- 战斗服务器
- 商城与支付服务

### 整体架构规划

整个项目规划为 **三大模块**。

#### 架构概览

- 🎮 **GamePlay 客户端框架**  
  运行在 Unity 内部，是可玩运行时基础。负责表现层、本地模拟承载、命令输入、地图/寻路集成，以及主线程视觉层与逻辑线程模拟层之间的桥接。

- 🌐 **基于 .NET Socket 的战斗服务器**  
  作为未来帧同步调度中心，负责房间管理、帧推进、输入分发、连接状态处理以及多人对战秩序维护。

- 💳 **基于 .NET 的商城与支付服务**  
  作为外围业务后端，负责商品管理、订单处理、支付渠道接入、回调处理以及账号资产更新。

#### 1. GamePlay 客户端框架

这是当前仓库中已经开始落地的部分，主要负责：

- 游戏运行时
- RTS 核心逻辑模拟
- 单位状态推进
- 命令派发
- 地图与寻路
- 逻辑结果与画面表现同步

该模块采用 **帧同步导向的设计思路**，并在客户端内部拆分为两个核心线程。

##### Unity 主线程

主要职责：

- 场景生命周期管理
- GameObject 与渲染对象管理
- 输入采集
- 调试显示
- 逻辑结果可视化呈现

Unity 主线程主要承担 **表现层** 职责，不直接承载复杂核心模拟计算。

##### Unity 逻辑线程

主要职责：

- 固定步进逻辑 Tick
- 单位状态推进
- 行为与活动调度
- 命令执行
- 寻路、导航、占位等纯逻辑计算
- 输出可供主线程消费的快照数据

逻辑线程强调 **可迁移、可控、可扩展** 的模拟核心，并尽量减少对 Unity API 的依赖。

##### 当前仓库中的设计映射

当前代码结构已经体现出这一设计方向：

- `Assets/Game/World/UnityWorld/World.cs`  
  Unity 侧桥接层，负责逻辑线程管理、命令收发、快照消费与表现驱动。

- `Assets/Game/World/Logic/LogicWorld.cs`  
  独立逻辑线程中的核心模拟世界，按 Tick 推进逻辑。

- `Assets/Game/Gameplay/Commands/`  
  命令构造、编码与调度，是后续帧同步输入流的重要基础。

- `Assets/Game/Spatial/Map/` 与 `Assets/Game/Spatial/Pathing/`  
  RTS 必需的地图与路径规划能力。

- `Assets/Game/Gameplay/Units/`  
  单位、行为、能力与活动的基础对象模型。

简而言之，项目仍处于早期阶段，但 **表现层 / 逻辑层分离** 的基础骨架已经可见。

#### 2. 基于 .NET Socket 的战斗服务器

第二个规划模块是 **帧同步战斗服务器**。

它的职责不是渲染，而是维护多人对战中的同步秩序，包括：

- 房间管理
- 玩家接入与断线处理
- 帧号推进
- 输入收集与广播
- 对局状态维护
- 超时控制与基础反作弊

在帧同步模型中，服务端扮演 **同步调度中心**，确保所有客户端在统一时间轴上接收一致输入。

这一层计划基于 .NET Socket 构建，以保持通信层轻量、稳定，并便于后续扩展房间、战斗、回放与观战等能力。

#### 3. 基于 .NET 的商城与支付服务

第三个规划模块是 **商城与支付服务层**。

其职责包括：

- 商品管理
- 订单管理
- 支付渠道接入
- 支付回调处理
- 账号资产变更
- 商城相关接口服务

与战斗服务器不同，这一模块不参与实时战斗同步，而是承载业务侧与运营侧能力。

将战斗同步与商城支付拆分，有助于保持系统边界清晰，并提升安全性与独立扩缩容能力。

### 当前阶段

当前项目更适合被理解为：

- RTS 框架原型
- 线程分层与逻辑分层实验场
- 未来客户端 + 战斗服 + 商城服三端体系的起点

它 **还不是** 一个玩法闭环完整的正式游戏项目。

当前阶段主要在验证：

- 主线程与逻辑线程分离是否稳定
- 逻辑核心是否能逐步脱离 Unity 表现层
- 命令驱动与 Tick 推进是否适合作为帧同步基础
- 地图、单位、行为、寻路系统能否以可扩展方式组织
- 架构是否能自然衔接 .NET 战斗服务器

### 当前客户端重点

#### 1. 世界驱动

`World` 与 `LogicWorld` 的分层建立了：

- 主线程世界
- 逻辑线程世界
- 线程间通信机制
- 基于快照的输出机制

这是整个 RTS 框架的基础。

#### 2. 命令驱动

命令模型与编码结构为统一输入流做准备。无论输入来自本地玩家、AI、网络同步帧还是测试脚本，最终都应尽量汇聚为进入逻辑世界的统一命令流。

#### 3. 单位与行为抽象

RTS 的核心在于大量单位在统一规则下执行移动、巡逻、警戒、攻击、导航与状态切换，因此单位、能力、活动与行为抽象会长期作为框架核心。

#### 4. 数据驱动

当前仓库已经具备 YAML 相关结构。长期来看，单位属性、世界参数、行为参数与技能参数都应逐步数据化，以减少硬编码并提升迭代效率。

### 设计原则

#### 1. 表现与逻辑分离

表现层负责 **看见什么**，逻辑层负责 **实际发生了什么**。两者应通过快照、事件与命令连接，而不是直接相互侵入。

#### 2. 先划清边界，再扩展玩法

在当前阶段，架构边界比快速堆叠玩法更重要。对 RTS 项目而言，一旦边界混乱，后续维护成本会随着功能增长快速上升。

#### 3. 以帧同步约束系统设计

既然目标包含帧同步，就必须从早期开始以 Tick 规则、输入结构、状态推进方式、确定性以及客户端/服务端职责划分来约束系统设计。

#### 4. 为逻辑服务端化预留迁移空间

即使当前逻辑仍运行在客户端线程中，也应尽量保持模拟核心的可迁移性，为未来向独立服务端模块演进预留空间。

### 当前已可见的技术基础

当前仓库已经能看到若干基础设施：

- `World` 与 `LogicWorld` 的双线程结构
- 命令工厂与命令编码能力
- 地图、寻路与调试辅助能力
- 单位、活动与能力等核心对象模型
- YAML 加载与样例配置资产

这些内容并不意味着系统已经完整，而是主要用于验证架构边界、线程模型与数据组织方式。

### 演进方向

未来计划包括：

- 更完整的单位生命周期管理
- 更稳定的命令排队与帧执行机制
- 客户端输入采集与同步接入
- .NET Socket 帧同步服务器原型
- 房间、对局与帧广播机制
- 回放、观战与日志校验能力
- 商城服务、订单服务与支付接入
- 更规范的数据配置与热更新支持

### 总结

`Burst Strike` 的目标不是单纯做一个 Unity RTS Demo，而是成长为一套职责边界清晰、可落地演进的 RTS 框架：

- 客户端负责表现与本地模拟承载
- 战斗服务器负责帧同步与对战调度
- 商城服务器负责支付与运营侧业务

当前仓库已经落地了这一愿景中最底层的一部分：即具备主线程 / 逻辑线程分层的 Unity 客户端基础框架，以及 RTS 核心模块的初步雏形。

后续开发将继续围绕 **跨平台**、**高性能**、**高扩展** 的总体目标推进.
