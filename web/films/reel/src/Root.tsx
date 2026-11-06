import React from 'react';
import {Composition} from 'remotion';
import {Reel} from './Reel';
import {DURATION, FPS, H, W} from './timeline';

// 1920x480 band, 30 fps, 32 beats at 118 BPM = 488 frames, loops seamlessly.
export const RemotionRoot: React.FC = () => {
  return <Composition id="Reel" component={Reel} durationInFrames={DURATION} fps={FPS} width={W} height={H} />;
};
