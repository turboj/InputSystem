#if (UNITY_STANDALONE || UNITY_EDITOR) && UNITY_ENABLE_STEAM_CONTROLLER_SUPPORT
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Input;
using UnityEngine.Experimental.Input.Controls;
using UnityEngine.Experimental.Input.Layouts;
using UnityEngine.Experimental.Input.LowLevel;
using UnityEngine.Experimental.Input.Plugins.Steam;
using UnityEngine.Experimental.Input.Utilities;
#if UNITY_EDITOR
using UnityEngine.Experimental.Input.Plugins.Steam.Editor;
#endif

////REVIEW: instead of testing on TestController, it would be much more fruitful to test on a controller generated by the VDF conversion code;
////        some of the testing here ultimately tests little other than test code

internal class SteamTests : InputTestFixture
{
    private TestSteamControllerAPI m_SteamAPI;

    public override void Setup()
    {
        base.Setup();
        m_SteamAPI = new TestSteamControllerAPI();
        SteamSupport.api = m_SteamAPI;
        InputSystem.RegisterLayout<TestController>(
            matches: new InputDeviceMatcher()
                .WithInterface(SteamController.kSteamInterface)
                .WithProduct("TestController"));
    }

    public override void TearDown()
    {
        base.TearDown();
        m_SteamAPI = null;

        SteamSupport.s_API = null;
        SteamSupport.s_InputDevices = null;
        SteamSupport.s_ConnectedControllers = null;
        SteamSupport.s_InputDeviceCount = 0;
        SteamSupport.s_OnUpdateHookedIn = false;
    }

    [Test]
    [Category("Devices")]
    public void Devices_CanDiscoverSteamControllers()
    {
        m_SteamAPI.controllers = new ulong[] {1};

        InputSystem.Update();

        Assert.That(InputSystem.devices,
            Has.Exactly(1).TypeOf<TestController>().And.With.Property("handle")
                .EqualTo(new SteamHandle<SteamController>(1)));

        m_SteamAPI.controllers = new ulong[] {1, 2};

        // Make sure update discovers newly added Steam controllers.
        InputSystem.Update();

        Assert.That(InputSystem.devices,
            Has.Exactly(1).TypeOf<TestController>().And.With.Property("handle")
                .EqualTo(new SteamHandle<SteamController>(1)));
        Assert.That(InputSystem.devices,
            Has.Exactly(1).TypeOf<TestController>().And.With.Property("handle")
                .EqualTo(new SteamHandle<SteamController>(2)));

        // Make sure each controller got its actions resolved.
        Assert.That(InputSystem.devices.OfType<TestController>(),
            Has.All.Matches((TestController x) => x.resolveActionsCount == 1));
    }

    [Test]
    [Category("Devices")]
    public void Devices_CanRemoveSteamControllers()
    {
        m_SteamAPI.controllers = new ulong[] {1, 2};

        InputSystem.Update();

        Assert.That(InputSystem.devices, Has.Exactly(2).TypeOf<TestController>());

        m_SteamAPI.controllers = new ulong[] {2};

        InputSystem.Update();

        Assert.That(InputSystem.devices,
            Has.None.TypeOf<TestController>().And.With.Property("handle")
                .EqualTo(new SteamHandle<SteamController>(1)));
        Assert.That(InputSystem.devices,
            Has.Exactly(1).TypeOf<TestController>().And.With.Property("handle")
                .EqualTo(new SteamHandle<SteamController>(2)));
    }

    [Test]
    [Category("Devices")]
    public void Devices_CanUpdateSteamControllers()
    {
        m_SteamAPI.controllers = new ulong[] {1};

        InputSystem.Update();
        InputSystem.Update();

        var device = (TestController)InputSystem.devices.First(x => x is TestController);

        Assert.That(device.updateCount, Is.EqualTo(2));
        Assert.That(m_SteamAPI.runFrameCount, Is.EqualTo(2));
    }

    // Option 1: Use SteamController-derived class directly to control active action sets.
    [Test]
    [Category("Devices")]
    public void Devices_CanManuallyActivateActionSetsOnSteamControllers()
    {
        m_SteamAPI.controllers = new ulong[] {1};
        InputSystem.Update();
        var device = (TestController)InputSystem.devices.First(x => x is TestController);

        device.ActivateActionSet(device.gameplaySetHandle);

        Assert.That(m_SteamAPI.actionSetActivations, Is.Not.Null.And.Length.EqualTo(1));
        Assert.That(m_SteamAPI.actionSetActivations[0], Is.EquivalentTo(new[] { device.gameplaySetHandle }));

        var current = device.GetCurrentActionSet();

        Assert.That(current, Is.EqualTo(device.gameplaySetHandle));
    }

    // Option 2: Have action sets on SteamControllers activate automatically in sync with InputActionMaps.
    [Test]
    [Category("Devices")]
    public void Devices_CanAutomaticallyActivateActionSetsOnSteamControllers()
    {
        var map = new InputActionMap("gameplay");
        map.AddAction("fire", binding: "<TestController>/fire");
        map.AddAction("look", binding: "<TestController>/look");

        m_SteamAPI.controllers = new ulong[] {1};
        InputSystem.Update();

        map.Enable();

        Assert.That(m_SteamAPI.actionSetActivations, Is.EquivalentTo(new[] {TestSteamControllerAPI.gameplaySet}));
    }

    [Test]
    [Category("Devices")]
    [Ignore("TODO")]
    public void TODO_Devices_CanActivateActionSetOnSteamController_WhenAlreadyActivatedAtTimeOfControllerCreation()
    {
        var map = new InputActionMap("gameplay");
        map.AddAction("fire", binding: "<TestController>/fire");
        map.AddAction("look", binding: "<TestController>/look");

        // First enable.
        map.Enable();

        // Then add controller.
        m_SteamAPI.controllers = new ulong[] {1};
        InputSystem.Update();

        Assert.Fail();
        //Assert.That(m_SteamAPI.actionSetActivations, Has.Count.EqualTo(1));
        //Assert.That(m_SteamAPI.actionSetActivations[0].Key, Is.EqualTo(1));
        //Assert.That(m_SteamAPI.actionSetActivations[0].Value, Is.EqualTo(TestSteamControllerAPI.gameplaySet));
    }

    [Test]
    [Category("Devices")]
    public void Devices_SteamControllersSendActionStateAsEvents()
    {
        var receivedStateEvent = false;
        InputSystem.onEvent +=
            eventPtr =>
        {
            if (!eventPtr.IsA<StateEvent>())
                return;
            var device = InputSystem.GetDeviceById(eventPtr.deviceId) as TestController;
            if (device == null)
                return;

            receivedStateEvent = true;
        };

        m_SteamAPI.AddController(1);

        InputSystem.Update();
        var controller = (TestController)InputSystem.devices.First(x => x is TestController);

        controller.ActivateActionSet(controller.gameplaySetHandle);

        Assert.That(controller.fire.isPressed, Is.False);

        // Press fire button.
        m_SteamAPI.controllerData[0].digitalData[TestSteamControllerAPI.fireAction] =
            new SteamDigitalActionData
        {
            pressed = true,
            active = true
        };

        InputSystem.Update();

        Assert.That(receivedStateEvent, Is.True);
        Assert.That(controller.fire.isPressed, Is.True);
    }

#if UNITY_EDITOR

    [Test]
    [Category("Editor")]
    public void Editor_CanGenerateInputDeviceBasedOnSteamIGAFile()
    {
        // Create an InputActions setup and convert it to Steam IGA.
        var asset = ScriptableObject.CreateInstance<InputActionAsset>();
        var actionMap1 = new InputActionMap("map1");
        var actionMap2 = new InputActionMap("map2");
        actionMap1.AddAction("buttonAction", expectedControlLayout: "Button");
        actionMap1.AddAction("axisAction", expectedControlLayout: "Axis");
        actionMap1.AddAction("stickAction", expectedControlLayout: "Stick");
        actionMap2.AddAction("vector2Action", expectedControlLayout: "Vector2");

        asset.AddActionMap(actionMap1);
        asset.AddActionMap(actionMap2);

        var vdf = SteamIGAConverter.ConvertInputActionsToSteamIGA(asset);

        // Generate a C# input device from the Steam IGA file.
        var generatedCode = SteamIGAConverter.GenerateInputDeviceFromSteamIGA(vdf, "My.Namespace.MySteamController");

        Assert.That(generatedCode, Does.StartWith("// THIS FILE HAS BEEN AUTO-GENERATED"));
        Assert.That(generatedCode, Contains.Substring("#if (UNITY_EDITOR || UNITY_STANDALONE) && UNITY_ENABLE_STEAM_CONTROLLER_SUPPORT"));
        Assert.That(generatedCode, Contains.Substring("namespace My.Namespace\n"));
        Assert.That(generatedCode, Contains.Substring("public class MySteamController : SteamController\n"));
        Assert.That(generatedCode, Contains.Substring("public unsafe struct MySteamControllerState : IInputStateTypeInfo\n"));
        Assert.That(generatedCode, Contains.Substring("[InitializeOnLoad]"));
        Assert.That(generatedCode, Contains.Substring("[RuntimeInitializeOnLoadMethod"));
        Assert.That(generatedCode, Contains.Substring("new FourCC('M', 'y', 'S', 't')"));
        Assert.That(generatedCode, Contains.Substring("protected override void FinishSetup(InputDeviceBuilder builder)"));
        Assert.That(generatedCode, Contains.Substring("base.FinishSetup(builder);"));
        Assert.That(generatedCode, Contains.Substring("new InputDeviceMatcher"));
        Assert.That(generatedCode, Contains.Substring("WithInterface(\"Steam\")"));
        Assert.That(generatedCode, Contains.Substring("public SteamHandle<InputActionMap> map1SetHandle"));
        Assert.That(generatedCode, Contains.Substring("public SteamHandle<InputActionMap> map2SetHandle"));
        Assert.That(generatedCode, Contains.Substring("public SteamHandle<InputAction> stickActionHandle"));
        Assert.That(generatedCode, Contains.Substring("public SteamHandle<InputAction> buttonActionHandle"));
        Assert.That(generatedCode, Contains.Substring("public SteamHandle<InputAction> axisActionHandle"));
        Assert.That(generatedCode, Contains.Substring("public SteamHandle<InputAction> vector2ActionHandle"));
        Assert.That(generatedCode, Contains.Substring("public StickControl stickAction"));
        Assert.That(generatedCode, Contains.Substring("public ButtonControl buttonAction"));
        Assert.That(generatedCode, Contains.Substring("public AxisControl axisAction"));
        Assert.That(generatedCode, Contains.Substring("public Vector2Control vector2Action"));
        Assert.That(generatedCode, Contains.Substring("stickAction = builder.GetControl<StickControl>(\"stickAction\");"));
        Assert.That(generatedCode, Contains.Substring("buttonAction = builder.GetControl<ButtonControl>(\"buttonAction\");"));
        Assert.That(generatedCode, Contains.Substring("axisAction = builder.GetControl<AxisControl>(\"axisAction\");"));
        Assert.That(generatedCode, Contains.Substring("vector2Action = builder.GetControl<Vector2Control>(\"vector2Action\");"));
        Assert.That(generatedCode, Contains.Substring("protected override void ResolveActions(ISteamControllerAPI api)"));
        Assert.That(generatedCode, Contains.Substring("map1SetHandle = api.GetActionSetHandle(\"map1\");"));
        Assert.That(generatedCode, Contains.Substring("map2SetHandle = api.GetActionSetHandle(\"map2\");"));
        Assert.That(generatedCode, Contains.Substring("buttonActionHandle = api.GetDigitalActionHandle(\"buttonAction\");"));
        Assert.That(generatedCode, Contains.Substring("axisActionHandle = api.GetAnalogActionHandle(\"axisAction\");"));
        Assert.That(generatedCode, Contains.Substring("stickActionHandle = api.GetAnalogActionHandle(\"stickAction\");"));
        Assert.That(generatedCode, Contains.Substring("vector2ActionHandle = api.GetAnalogActionHandle(\"vector2Action\");"));
        Assert.That(generatedCode, Contains.Substring("protected override void Update(ISteamControllerAPI api)"));
    }

    [Test]
    [Category("Editor")]
    public void Editor_CanConvertInputActionsToSteamIGAFormat()
    {
        var asset = ScriptableObject.CreateInstance<InputActionAsset>();
        var actionMap1 = new InputActionMap("map1");
        var actionMap2 = new InputActionMap("map2");
        actionMap1.AddAction("buttonAction", expectedControlLayout: "Button");
        actionMap1.AddAction("axisAction", expectedControlLayout: "Axis");
        actionMap1.AddAction("stickAction", expectedControlLayout: "Stick");
        actionMap2.AddAction("vector2Action", expectedControlLayout: "Vector2");

        asset.AddActionMap(actionMap1);
        asset.AddActionMap(actionMap2);

        var vdf = SteamIGAConverter.ConvertInputActionsToSteamIGA(asset);
        var dictionary = SteamIGAConverter.ParseVDF(vdf);

        // Top-level key "In Game Actions".
        Assert.That(dictionary.Count, Is.EqualTo(1));
        Assert.That(dictionary, Contains.Key("In Game Actions").With.TypeOf<Dictionary<string, object>>());

        // "actions" and "localization" inside "In Game Actions".
        var inGameActions = (Dictionary<string, object>)dictionary["In Game Actions"];
        Assert.That(inGameActions, Contains.Key("actions"));
        Assert.That(inGameActions["actions"], Is.TypeOf<Dictionary<string, object>>());
        Assert.That(inGameActions, Contains.Key("localization"));
        Assert.That(inGameActions["localization"], Is.TypeOf<Dictionary<string, object>>());
        Assert.That(inGameActions.Count, Is.EqualTo(2));

        // Two action maps inside "actions".
        var actions = (Dictionary<string, object>)inGameActions["actions"];
        Assert.That(actions, Contains.Key("map1"));
        Assert.That(actions["map1"], Is.TypeOf<Dictionary<string, object>>());
        Assert.That(actions, Contains.Key("map2"));
        Assert.That(actions["map2"], Is.TypeOf<Dictionary<string, object>>());
        Assert.That(actions.Count, Is.EqualTo(2));

        // Three actions inside "map1".
        var map1 = (Dictionary<string, object>)actions["map1"];
        Assert.That(map1, Contains.Key("title"));
        Assert.That(map1, Contains.Key("StickPadGyro"));
        Assert.That(map1, Contains.Key("AnalogTrigger"));
        Assert.That(map1, Contains.Key("Button"));
        Assert.That(map1.Count, Is.EqualTo(4));
        Assert.That(map1["title"], Is.EqualTo("#Set_map1"));
        Assert.That(map1["StickPadGyro"], Is.TypeOf<Dictionary<string, object>>());
        Assert.That(map1["AnalogTrigger"], Is.TypeOf<Dictionary<string, object>>());
        Assert.That(map1["Button"], Is.TypeOf<Dictionary<string, object>>());
        var stickPadGyro1 = (Dictionary<string, object>)map1["StickPadGyro"];
        Assert.That(stickPadGyro1, Has.Count.EqualTo(1));
        Assert.That(stickPadGyro1, Contains.Key("stickAction"));
        Assert.That(stickPadGyro1["stickAction"], Is.TypeOf<Dictionary<string, object>>());
        var stickAction = (Dictionary<string, object>)stickPadGyro1["stickAction"];
        Assert.That(stickAction, Contains.Key("title"));
        Assert.That(stickAction, Contains.Key("input_mode"));
        Assert.That(stickAction.Count, Is.EqualTo(2));
        Assert.That(stickAction["title"], Is.EqualTo("#Action_map1_stickAction"));
        Assert.That(stickAction["input_mode"], Is.EqualTo("joystick_move"));

        // One action inside "map2".
        var map2 = (Dictionary<string, object>)actions["map2"];
        Assert.That(map2, Contains.Key("title"));
        Assert.That(map2["title"], Is.EqualTo("#Set_map2"));

        // Localization strings.
        var localization = (Dictionary<string, object>)inGameActions["localization"];
        Assert.That(localization.Count, Is.EqualTo(1));
        Assert.That(localization, Contains.Key("english"));
        Assert.That(localization["english"], Is.TypeOf<Dictionary<string, object>>());
        var english = (Dictionary<string, object>)localization["english"];
        Assert.That(english, Contains.Key("Set_map1"));
        Assert.That(english, Contains.Key("Set_map2"));
        Assert.That(english, Contains.Key("Action_map1_buttonAction"));
        Assert.That(english, Contains.Key("Action_map1_axisAction"));
        Assert.That(english, Contains.Key("Action_map1_stickAction"));
        Assert.That(english, Contains.Key("Action_map2_vector2Action"));
        Assert.That(english["Set_map1"], Is.EqualTo("map1"));
        Assert.That(english["Set_map2"], Is.EqualTo("map2"));
        Assert.That(english["Action_map1_buttonAction"], Is.EqualTo("buttonAction"));
        Assert.That(english["Action_map1_axisAction"], Is.EqualTo("axisAction"));
        Assert.That(english["Action_map1_stickAction"], Is.EqualTo("stickAction"));
        Assert.That(english["Action_map2_vector2Action"], Is.EqualTo("vector2Action"));
        Assert.That(english.Count, Is.EqualTo(6));
    }

    [Test]
    [Category("Editor")]
    [Ignore("TODO")]
    public void TODO_Editor_ConvertingInputActionsToSteamIGA_ThrowsIfTwoActionsHaveTheSameName()
    {
        Assert.Fail();
    }

#endif

    struct TestControllerState : IInputStateTypeInfo
    {
        [InputControl(layout = "Button")]
        public bool fire;
        [InputControl(layout = "Stick")]
        public Vector2 look;

        public FourCC GetFormat()
        {
            return new FourCC('T', 'e', 's', 't');
        }
    }

    [InputControlLayout(stateType = typeof(TestControllerState))]
    class TestController : SteamController
    {
        public ButtonControl fire { get; private set; }
        public StickControl look { get; private set; }

        public int updateCount;
        public int resolveActionsCount;

        public SteamHandle<InputActionMap> gameplaySetHandle;
        public SteamHandle<InputAction> fireActionHandle;
        public SteamHandle<InputAction> lookActionHandle;

        protected override void FinishSetup(InputDeviceBuilder builder)
        {
            base.FinishSetup(builder);
            fire = builder.GetControl<ButtonControl>("fire");
            look = builder.GetControl<StickControl>("look");
        }

        protected override void ResolveActions(ISteamControllerAPI api)
        {
            ++resolveActionsCount;
            gameplaySetHandle = api.GetActionSetHandle("gameplay");
            fireActionHandle = api.GetDigitalActionHandle("fire");
            lookActionHandle = api.GetDigitalActionHandle("look");
        }

        protected override void Update(ISteamControllerAPI api)
        {
            ++updateCount;

            var fireActionData = api.GetDigitalActionData(handle, fireActionHandle);
            var lookActionData = api.GetAnalogActionData(handle, lookActionHandle);

            InputSystem.QueueStateEvent(this,
                new TestControllerState
                {
                    fire = fireActionData.pressed,
                    look = lookActionData.position,
                });
        }
    }

    ////REVIEW: this could really benefit from using a mocking library
    class TestSteamControllerAPI : ISteamControllerAPI
    {
        public int runFrameCount;
        public ulong[] controllers;
        public SteamHandle<InputActionMap>[][] actionSetActivations;

        public struct ControllerData
        {
            public Dictionary<SteamHandle<InputAction>, SteamAnalogActionData> analogData;
            public Dictionary<SteamHandle<InputAction>, SteamDigitalActionData> digitalData;
        }

        public ControllerData[] controllerData;

        public static SteamHandle<InputAction> fireAction = new SteamHandle<InputAction>(11);
        public static SteamHandle<InputAction> lookAction = new SteamHandle<InputAction>(12);
        public static SteamHandle<InputActionMap> gameplaySet = new SteamHandle<InputActionMap>(13);

        public Dictionary<string, SteamHandle<InputAction>> digitalActions =
            new Dictionary<string, SteamHandle<InputAction>>
        {
            {"fire", fireAction}
        };

        public Dictionary<string, SteamHandle<InputAction>> analogActions =
            new Dictionary<string, SteamHandle<InputAction>>
        {
            {"look", lookAction}
        };

        public Dictionary<string, SteamHandle<InputActionMap>> sets =
            new Dictionary<string, SteamHandle<InputActionMap>>
        {
            {"gameplay", gameplaySet}
        };

        public void AddController(ulong handle)
        {
            ArrayHelpers.Append(ref controllers, handle);
            var index = ArrayHelpers.Append(ref controllerData, new ControllerData());
            controllerData[index].analogData = new Dictionary<SteamHandle<InputAction>, SteamAnalogActionData>();
            controllerData[index].digitalData = new Dictionary<SteamHandle<InputAction>, SteamDigitalActionData>();
        }

        public void RunFrame()
        {
            ++runFrameCount;
        }

        public int GetConnectedControllers(SteamHandle<SteamController>[] outHandles)
        {
            Debug.Assert(outHandles.Length == 16);
            if (controllers == null)
                return 0;
            for (var i = 0; i < controllers.Length; ++i)
                outHandles[i] = new SteamHandle<SteamController>(controllers[i]);
            return controllers.Length;
        }

        public SteamHandle<InputActionMap> GetActionSetHandle(string actionSetName)
        {
            SteamHandle<InputActionMap> result;
            sets.TryGetValue(actionSetName, out result);
            return result;
        }

        public SteamHandle<InputAction> GetDigitalActionHandle(string actionName)
        {
            SteamHandle<InputAction> result;
            digitalActions.TryGetValue(actionName, out result);
            return result;
        }

        public SteamHandle<InputAction> GetAnalogActionHandle(string actionName)
        {
            SteamHandle<InputAction> result;
            analogActions.TryGetValue(actionName, out result);
            return result;
        }

        public SteamAnalogActionData GetAnalogActionData(SteamHandle<SteamController> controllerHandle,
            SteamHandle<InputAction> analogActionHandle)
        {
            for (var i = 0; i < controllers.Length; ++i)
            {
                if (controllers[i] != (ulong)controllerHandle)
                    continue;

                SteamAnalogActionData result;
                if (controllerData[i].analogData.TryGetValue(analogActionHandle, out result))
                    return result;
            }

            return new SteamAnalogActionData();
        }

        public SteamDigitalActionData GetDigitalActionData(SteamHandle<SteamController> controllerHandle,
            SteamHandle<InputAction> digitalActionHandle)
        {
            for (var i = 0; i < controllers.Length; ++i)
            {
                if (controllers[i] != (ulong)controllerHandle)
                    continue;

                SteamDigitalActionData result;
                if (controllerData[i].digitalData.TryGetValue(digitalActionHandle, out result))
                    return result;
            }

            return new SteamDigitalActionData();
        }

        public void ActivateActionSet(SteamHandle<SteamController> controllerHandle, SteamHandle<InputActionMap> actionSetHandle)
        {
            var index = Array.IndexOf(controllers, (ulong)controllerHandle);
            Array.Resize(ref actionSetActivations, controllers.Length);
            actionSetActivations[index] = new[] {actionSetHandle};
        }

        public SteamHandle<InputActionMap> GetCurrentActionSet(SteamHandle<SteamController> controllerHandle)
        {
            var index = Array.IndexOf(controllers, (ulong)controllerHandle);
            return actionSetActivations[index][actionSetActivations[index].Length - 1];
        }

        public void ActivateActionSetLayer(SteamHandle<SteamController> controllerHandle, SteamHandle<InputActionMap> actionSetLayerHandle)
        {
            throw new NotImplementedException();
        }

        public void DeactivateActionSetLayer(SteamHandle<SteamController> controllerHandle, SteamHandle<InputActionMap> actionSetLayerHandle)
        {
            throw new NotImplementedException();
        }

        public void DeactivateAllActionSetLayers(SteamHandle<SteamController> controllerHandle)
        {
            throw new NotImplementedException();
        }

        public int GetActiveActionSetLayers(SteamHandle<SteamController> controllerHandle,
            out SteamHandle<InputActionMap> handlesOut)
        {
            throw new NotImplementedException();
        }
    }
}

#endif // (UNITY_STANDALONE || UNITY_EDITOR) && UNITY_ENABLE_STEAM_CONTROLLER_SUPPORT
