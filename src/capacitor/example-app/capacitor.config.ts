import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'ai.ansight.capacitor.harness',
  appName: 'Ansight Capacitor Harness',
  webDir: 'dist',
  server: {
    androidScheme: 'https',
  },
};

export default config;
