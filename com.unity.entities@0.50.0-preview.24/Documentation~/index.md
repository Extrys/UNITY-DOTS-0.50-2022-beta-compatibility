---
uid: ecs-overview
---
# Entities overview
The Entities package adds functionality to your Unity project that lets you use Unity's Entity Component System (ECS). The ECS system organizes your project in a data-oriented way, as opposed to the traditional object-oriented way.

## Entity Component System

<div id="slides">
    <style type="text/css" scoped="">
        .infographic {background-color: #020202;}
        a {text-decoration: none; display: inline-block; padding: 8px 16px;}
        a:hover {background-color: #ddd; color: black;}
        .previous {background-color: #4CAF50; color: black;}
        .next {background-color: #4CAF50; color: black;}
        .round {border-radius: 50%;}
        .slideshow {display: flex; align-items: center;}
    </style>
    <div class="slideshow">
        <a href="#" id="previous" class="previous round">&#8249;</a>
        <img class="infographic" src="images/WhatIsECSinfographic0000.png">
        <img class="infographic" src="images/WhatIsECSinfographic0001.png">
        <img class="infographic" src="images/WhatIsECSinfographic0002.png">
        <img class="infographic" src="images/WhatIsECSinfographic0003.png">
        <img class="infographic" src="images/WhatIsECSinfographic0004.png">
        <a href="#" id="next" class="next round">&#8250;</a> 
    </div>
    <script type="text/javascript" src="images/infographic.js"> </script>
</div>


ECS is the core of the Unity Data-Oriented Tech Stack. As the name indicates, ECS has three principal parts:

* [Entities](ecs_entities.md): The entities, or things, that populate your game or program.
* [Components](ecs_components.md): The data associated with your entities, but organized by the data itself rather than by entity. This difference in organization is one of the key differences  between an object-oriented and a data-oriented design.
* [Systems](ecs_systems.md): The logic that transforms the component data from its current state to its next state. For example, a system might update the positions of all moving entities by their velocity times the time interval since the previous frame.


