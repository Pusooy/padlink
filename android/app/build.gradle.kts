plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.padlink.app"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.padlink.app"
        minSdk = 26              // Android 8.0+（VibrationEffect 原生支持，覆盖 99%+ 设备）
        targetSdk = 36           // Android 15；Android 16/17 上走兼容模式，无启动限制
        versionCode = 4
        versionName = "1.3"
        resourceConfigurations += listOf("zh", "en")
    }

    // AGP 8.5+ 的 16KB page size 支持开关
    androidResources {
        // 让 resources.arsc 也按 16KB 对齐（Android 17 校验项之一）
        noCompress.add("arsc")
    }

    buildTypes {
        getByName("release") {
            isMinifyEnabled = false
            // debug 与 release 用同一个密钥，保证覆盖安装不冲突
            signingConfig = signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    packaging {
        resources.excludes += setOf(
            "META-INF/*.kotlin_module",
            "META-INF/*.version",
            "META-INF/DEPENDENCIES",
        )
        // Android 15+ 要求 16KB page size 对齐；Android 17 将校验设为强制，
        // 未对齐会导致 PackageManager 报 "Error occurred while checking alignment"
        // 并使 Activity 无法解析（Error type 3 / result code=-92）。
        jniLibs.useLegacyPackaging = false
        // 关键：dex 必须 STORED（不压缩）才能按 16KB 对齐
        dex.useLegacyPackaging = false
    }
    buildFeatures {
        buildConfig = true
    }
    lint {
        abortOnError = false
    }
}

dependencies {
    // 仅保留 OkHttp —— 源码中没有任何 androidx 引用，
    // core-ktx 会在 Application 阶段自动初始化组件，在 Android 16/17 上
    // 有启动期崩溃风险，移除后启动链路最简。
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
}
