const React = require('react');
const { Dimensions, PixelRatio, findNodeHandle, NativeModules } = require('react-native');
const { pointerProperties } = require('@ansight/pencil-runtime');
const names = { down: 'onPointerDown', move: 'onPointerMove', up: 'onPointerUp', cancel: 'onPointerCancel', hoverEnter: 'onPointerEnter', hoverMove: 'onPointerMove', hoverExit: 'onPointerLeave' };
const touchNames = {down:'onTouchStart',move:'onTouchMove',up:'onTouchEnd',cancel:'onTouchCancel'};

function usePencilSurface(runtime, id, ref, handlers) {
  const latest = React.useRef(handlers);
  latest.current = handlers;
  const real = React.useRef(new Set());
  React.useEffect(() => {
    const mountedView = ref.current;
    const dispose = runtime.bindSurface(id, {
      validate(samples) {
        for (const sample of samples) if (typeof latest.current[names[sample.phase]] !== 'function' && typeof latest.current[touchNames[sample.phase]] !== 'function') throw new Error(`The Pencil surface has no handler for ${sample.phase}.`);
        if (samples.some(sample => sample.phase === 'down') && typeof latest.current.onPointerCancel !== 'function' && typeof latest.current.onTouchCancel !== 'function') throw new Error('Supply onPointerCancel or onTouchCancel so interrupted strokes can be released.');
      },
      snapshot: () => new Promise((resolve, reject) => {
        if (!mountedView || ref.current !== mountedView) { reject(new Error('Pencil view is unmounted or replaced.')); return; }
        const timer = setTimeout(() => reject(new Error('Pencil view measurement timed out.')), 1000);
        mountedView.measureInWindow((x, y, width, height) => {
          clearTimeout(timer);
          const viewport = Dimensions.get('window');
          resolve({x, y, width, height, viewportWidth: viewport.width, viewportHeight: viewport.height, scale: PixelRatio.get()});
        });
      }),
      hitTest: point => NativeModules.AnsightReactNativePencil.hitTest(id, point.x, point.y),
      isBusy: () => real.current.size > 0,
      dispatch(sample, rect, pointerId) {
        const callback = latest.current[names[sample.phase]];
        const touchCallback = latest.current[touchNames[sample.phase]];
        if (!callback && sample.phase === 'hoverExit') return;
        if (!callback && !touchCallback) throw new Error(`The Pencil surface has no handler for ${sample.phase}.`);
        const nativeEvent = { ...pointerProperties(sample, rect, pointerId), target: findNodeHandle(mountedView), timestamp: Date.now() };
        nativeEvent.locationX = nativeEvent.clientX - rect.x;
        nativeEvent.locationY = nativeEvent.clientY - rect.y;
        nativeEvent.offsetX = nativeEvent.locationX;
        nativeEvent.offsetY = nativeEvent.locationY;
        let stopped = false, prevented = false;
        const event = { nativeEvent, target: nativeEvent.target, currentTarget: nativeEvent.target, timeStamp: nativeEvent.timestamp,
          type: names[sample.phase].slice(2).toLowerCase(), eventPhase: 2,
          bubbles: true, cancelable: true, isTrusted: false, get defaultPrevented() { return prevented; },
          preventDefault() { prevented = true; }, stopPropagation() { stopped = true; }, persist() {},
          isDefaultPrevented: () => prevented, isPropagationStopped: () => stopped };
        callback?.(event);
        if (touchCallback) {
          const touch = {...nativeEvent, identifier: pointerId, force: sample.pressure};
          touchCallback({...event, type: touchNames[sample.phase].slice(2).toLowerCase(), nativeEvent: {
            ...touch, changedTouches:[touch], touches:['up','cancel'].includes(sample.phase) ? [] : [touch],
          }});
        }
      },
      onError: error => console.error('Ansight Pencil cleanup failed', error),
    });
    return () => { dispose().catch(error => console.error('Ansight Pencil cleanup failed', error)); };
  }, [runtime, id, ref]);
  return { ...handlers, testID: id, collapsable: false,
    onTouchStart(event) { for (const touch of event.nativeEvent.changedTouches) real.current.add(touch.identifier); handlers.onTouchStart?.(event); },
    onTouchEnd(event) { for (const touch of event.nativeEvent.changedTouches) real.current.delete(touch.identifier); handlers.onTouchEnd?.(event); },
    onTouchCancel(event) { for (const touch of event.nativeEvent.changedTouches) real.current.delete(touch.identifier); handlers.onTouchCancel?.(event); },
  };
}
module.exports = { usePencilSurface };
