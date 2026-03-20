import React, { useEffect, useState } from "react";
import { Button, SafeAreaView, ScrollView, Text, View } from "react-native";

import { Ansight, type AnsightDebugSnapshot } from "@ansight/react-native";

export default function App() {
  const [snapshot, setSnapshot] = useState<AnsightDebugSnapshot | null>(null);

  const refresh = async () => {
    setSnapshot(await Ansight.getDebugSnapshot());
  };

  useEffect(() => {
    void refresh();
  }, []);

  return (
    <SafeAreaView>
      <ScrollView contentInsetAdjustmentBehavior="automatic">
        <View style={{ padding: 20, gap: 12 }}>
          <Text style={{ fontSize: 24, fontWeight: "600" }}>Ansight React Native Harness</Text>
          <Button
            title="Initialize"
            onPress={async () => {
              await Ansight.initialize();
              await refresh();
            }}
          />
          <Button
            title="Activate"
            onPress={async () => {
              await Ansight.activate();
              await refresh();
            }}
          />
          <Button
            title="Record metric"
            onPress={async () => {
              await Ansight.metric(Date.now() % 10_000, 42);
              await refresh();
            }}
          />
          <Button
            title="Record event"
            onPress={async () => {
              await Ansight.event("react_native_harness_tapped", {
                type: "Navigation",
                details: "source=react-native-harness",
                channel: 42,
              });
              await refresh();
            }}
          />
          <Button
            title="Open harness session"
            onPress={async () => {
              await Ansight.openSession('{"schema":"ansight.pairing-config.v1"}', {
                clientName: "React Native Harness",
                manualHostAddress: "127.0.0.1",
              });
              await refresh();
            }}
          />
          <Button
            title="Clear buffers"
            onPress={async () => {
              await Ansight.clear();
              await refresh();
            }}
          />
          <Text selectable style={{ fontFamily: "Menlo", fontSize: 12 }}>
            {JSON.stringify(snapshot, null, 2)}
          </Text>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}
