# XR-Based Wearable Navigation Assistance System

## Overview
This project focuses on the development of a **wearable eXtended Reality (XR) guidance system** designed to improve safe and independent navigation for visually impaired individuals. Traditional mobility aids such as canes and guide dogs can be difficult to use in certain environments, while many existing technological solutions offer limited functionality and are financially inaccessible. This system is intended to complement existing aids by leveraging recent advancements in **XR** and **eXtended Intelligence (XI)**, including computer vision and machine learning.

Unlike conventional augmented reality solutions, XR expands user perception by blending virtual, augmented, and mixed realities. This approach enables the system to provide enhanced visual, auditory, and tactile awareness of the surrounding environment—information that may otherwise be inaccessible to visually impaired users.

## 🎥 Demo Video

Click the image below to watch a live demonstration of the XR-based navigation system:

[![XR Navigation System Demo](Media/demo_thumbnail.png)](https://drive.google.com/file/d/1zntz51qEC52iryslm50NT9Arzl1gq3_e/view?usp=sharing)

## Key Features
- **Real-Time Obstacle Detection**  
  Custom machine learning models detect and track obstacles within the user’s field of view, enabling rapid and reliable environmental awareness.

- **Multimodal Feedback System**  
  The device delivers feedback through a combination of:
  - Visual overlays
  - Spatialized audio cues
  - Haptic vibrations  

  Note: These modalities can be used independently or together to accommodate varying levels of visual impairment.

- **GPS Navigation Integration (WIP)**  
  Built-in navigation support provides directional guidance and route awareness directly within the XR interface.

## Hardware Platform
The system is implemented on the **Meta Quest 3 headset**, selected for its compact design, integrated sensors, and proven reliability as a consumer XR device. Embedding the solution directly into this headset removes the need for external peripherals, resulting in a streamlined and wearable eyewear-based system.

## User Interface and Accessibility
The interface overlays navigational cues and obstacle highlights onto the user’s live view in an intuitive manner. Accessibility is a core design focus:
- Users can customize or combine visual, audio, and haptic feedback modes.
- Vibration motors found in the handheld controllers provide directional and intensity-based haptic cues to convey obstacle location.
- Multimodal feedback ensures redundancy and clarity, enhancing user safety and confidence.

## Budget and Prototype
The project aims to deliver a fully functional prototype within a **$800 budget**, prioritizing affordability while maintaining performance and usability.

## Applications and Impact
Although primarily designed for visually impaired users, the system has broader applications in complex environments such as construction sites, crowded urban areas, and low-visibility conditions. By enhancing situational awareness and navigation confidence, the project demonstrates how XR technologies can serve as an everyday assistive tool for a wide range of users.

## Goal
The ultimate goal is to create an **accessible, affordable, and effective XR navigation solution** that enhances user safety, autonomy, and confidence while extending the benefits of XR technology to a broader population.

| Obstacle Detection | GPS Navigation (WIP) |
|:-------------:|:--------------------:|
| ![GIF 1](./Media/ObstacleDetection.gif) | ![GIF 2](./Media/Navigation.gif) | 
> [!NOTE]
> You must use a physical headset to preview the passthrough camera. XR Simulator and Meta Horizon Link do not currently support passthrough cameras.
