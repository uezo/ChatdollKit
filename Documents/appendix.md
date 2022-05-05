# Appendix 1. Setup ModelController manually

Create a new empty GameObject attach `OVR Lip Sync Context` and `OVR Lip Sync Context Morph Target`.

- Then turn on `Audio Loopback` in `OVR Lip Sync Context`
- Set the object that has the shapekeys for face expressions to `Skinned Mesh Renderer` in `OVR Lip Sync Context Morph Target`
- Configure viseme to blend targets in `OVR Lip Sync Context Morph Target`

<img src="https://uezo.blob.core.windows.net/github/chatdoll/02_2.png" width="640">

After that, select root GameObject to which ModelController is attached.

- Set LipSync object to `Audio Source`
- Set the object that has the shape keys for face expression to `Skinned Mesh Renderer`
- Set the shape key that close the eyes for blink to `Blink Blend Shape Name`.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/06_2.png" width="640">

# Appendix 2. Setup Animator manually

Create Animator Controller and create `Default` state on the Base Layer, then put animations. Lastly set a motion you like to the `Default` state. You can create other layers and put animations at this time. Note that every layers should have the `Default` state and `None` should be set to their motion except for the Base Layer.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/04.png" width="640">

After configuration set the Animator Controller as a `Controller` of Animator component of the 3D model.

<img src="https://uezo.blob.core.windows.net/github/chatdoll/05_2.png" width="640">


# Appendix 3. Using uLipSync

If you want to use uLipSync instead of OVRLipSync please follow the official readme. (Apple Store doesn't accept the app using OVRLipSync🙃)

https://github.com/hecomi/uLipSync

We don't provide LipSyncHelper for it because it doesn't have a function to reset viseme. But don't worry, it works without any helpers. 
