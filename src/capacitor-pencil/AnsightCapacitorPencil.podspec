Pod::Spec.new do |s|
  s.name = 'AnsightCapacitorPencil'
  s.version = '1.9.0'
  s.summary = 'Optional Ansight Pencil simulation for Capacitor on iOS Simulator.'
  s.license = { :type => 'PolyForm Shield 1.0.0', :file => 'LICENSE' }
  s.homepage = 'https://github.com/ansight-ai/ansight-sdk'
  s.author = { 'Ansight' => 'hello@ansight.ai' }
  s.source = { :git => 'https://github.com/ansight-ai/ansight-sdk.git', :tag => s.version.to_s }
  s.source_files = 'ios/Sources/AnsightCapacitorPencilPlugin/**/*.{swift,h,m,c,cc,mm,cpp}'
  s.ios.deployment_target = '15.0'
  s.swift_version = '5.9'
  s.dependency 'Capacitor'
  s.dependency 'AnsightPencil', s.version.to_s
end
