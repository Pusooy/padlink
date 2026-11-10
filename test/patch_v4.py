# -*- coding: utf-8 -*-
"""为 index.html 注入屏幕/文件/剪贴板/电源/主机状态的 JS 逻辑。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app\wwwroot\index.html"
s = io.open(P, encoding="utf-8").read()

js = r'''
/* ========= 屏幕页（看屏幕 + 点按定位） ========= */
var screenTimer=null, screenAuto=false;
function loadScreen(){
  var wrap=$("screenWrap"), img=$("screenImg");
  var url="/screen?w=1280&q=70&t="+Date.now();
  var pre=new Image();
  pre.onload=function(){
    img.src=url; wrap.classList.add("live");
    $("screenInfo").textContent = pre.naturalWidth+"×"+pre.naturalHeight+" · 点按画面=点击";
  };
  pre.onerror=function(){ $("screenInfo").textContent="截屏失败"; };
  pre.src=url;
}
$("screenRefresh").addEventListener("click",function(){ loadScreen(); buzz("tap") });
$("screenAuto").addEventListener("click",function(){
  screenAuto=!screenAuto;
  this.classList.toggle("on",screenAuto);
  this.style.color=screenAuto?"var(--accent)":"";
  if(screenAuto){ loadScreen(); screenTimer=setInterval(loadScreen,1500) }
  else { clearInterval(screenTimer); screenTimer=null }
  buzz("page");
});
$("screenImg").addEventListener("click",function(e){
  var r=this.getBoundingClientRect();
  var nx=(e.clientX-r.left)/r.width, ny=(e.clientY-r.top)/r.height;
  send({t:"screen_click",x:nx,y:ny,btn:"left"});
  buzz("left");
  var wr=$("screenWrap").getBoundingClientRect();
  var rp=document.createElement("div"); rp.className="screenRipple";
  rp.style.left=(e.clientX-wr.left)+"px";
  rp.style.top=(e.clientY-wr.top)+"px";
  $("screenWrap").appendChild(rp); setTimeout(function(){rp.remove()},520);
});

/* ========= 文件页 ========= */
var fileCwd="", fileParent=null;
function fmtSize(n){
  if(!n) return "";
  if(n<1024) return n+" B";
  if(n<1048576) return (n/1024).toFixed(0)+" KB";
  if(n<1073741824) return (n/1048576).toFixed(1)+" MB";
  return (n/1073741824).toFixed(2)+" GB";
}
function fileIcon(entry){
  if(entry.dir) return "folder";
  var k=entry.ext;
  if(["jpg","jpeg","png","gif","webp","bmp"].indexOf(k)>=0) return "image";
  if(["mp4","webm","mkv","mov","avi"].indexOf(k)>=0) return "video";
  if(["mp3","wav","flac","m4a","ogg"].indexOf(k)>=0) return "music";
  if(["zip","rar","7z","tar","gz"].indexOf(k)>=0) return "archive";
  if(["pdf"].indexOf(k)>=0) return "file-text";
  return "file";
}
function loadFiles(path){
  fetch("/files?path="+encodeURIComponent(path||""))
    .then(function(r){return r.json()})
    .then(function(d){
      if(d.error){ toast(d.error); return }
      fileCwd=d.path||""; fileParent=d.parent;
      $("filePath").textContent=fileCwd||"选择位置";
      var list=$("fileList"); list.innerHTML="";
      if(d.roots && d.roots.length){
        d.roots.forEach(function(root){
          list.appendChild(fileRow({name:root.name,path:root.path,dir:true,size:0,ext:""}));
        });
        icons();
        return;
      }
      var entries=d.entries||[];
      entries.forEach(function(en){ list.appendChild(fileRow(en)) });
      if(!entries.length) list.innerHTML='<div class="fItem"><div class="fn" style="color:var(--text3)">空目录</div></div>';
      icons();
    })
    .catch(function(){ toast("读取目录失败") });
}
function fileRow(en){
  var row=document.createElement("div"); row.className="fItem";
  var ic=document.createElement("div"); ic.className="fi";
  ic.innerHTML='<i data-lucide="'+fileIcon(en)+'"></i>';
  var nm=document.createElement("div"); nm.className="fn";
  nm.textContent = en.dir ? (en.name+"/") : en.name;
  var sz=document.createElement("div"); sz.className="fs";
  sz.textContent=en.dir?"":fmtSize(en.size);
  row.appendChild(ic); row.appendChild(nm); row.appendChild(sz);
  if(!en.dir){
    var open=document.createElement("button"); open.className="fx"; open.title="用电脑打开";
    open.innerHTML='<i data-lucide="external-link"></i>';
    open.addEventListener("click",function(ev){
      ev.stopPropagation(); send({t:"open_file",path:en.path}); buzz("tap");
    });
    row.appendChild(open);
  }
  row.addEventListener("click",function(){
    buzz("tap");
    if(en.dir) loadFiles(en.path); else previewFile(en);
  });
  return row;
}
var TEXT_EXT=["txt","md","log","json","xml","csv","js","ts","py","cs","java","c","cpp","h",
              "html","css","sql","bat","ps1","ini","yml","yaml","cfg"];
function previewFile(en){
  var pv=$("filePreview"), body=$("pvBody"), name=$("pvName");
  pv.style.display="flex"; name.textContent=en.name;
  body.innerHTML='<span style="color:var(--text3)">加载中…</span>';
  var k=fileIcon(en);
  var url="/file?path="+encodeURIComponent(en.path);
  if(k==="image"){
    body.innerHTML='<img src="'+url+'" alt="">';
  }else if(k==="video"){
    body.innerHTML='<video src="'+url+'" controls playsinline></video>';
  }else if(k==="music"){
    body.innerHTML='<audio src="'+url+'" controls></audio>';
  }else if(TEXT_EXT.indexOf(en.ext)>=0){
    fetch(url).then(function(r){return r.json()}).then(function(d){
      body.textContent=d.text||"";
    }).catch(function(){ body.textContent="读取失败" });
  }else{
    body.innerHTML='<div style="color:var(--text2);font-size:13px">该类型不支持在线预览<br><br>'+
      '<a href="'+url+'" download style="color:var(--accent)">下载到手机</a></div>';
  }
}
$("pvClose").addEventListener("click",function(){ $("filePreview").style.display="none" });
$("fileUp").addEventListener("click",function(){ loadFiles(fileParent||"") });
$("fileUploadBtn").addEventListener("click",function(){ $("fileInput").click() });
$("fileInput").addEventListener("change",function(){
  var files=this.files; if(!files.length) return;
  var dir=fileCwd||"";
  if(!dir){ toast("请先进入一个目录再上传"); this.value=""; return }
  var done=0, ok=0, total=files.length;
  for(var i=0;i<total;i++){
    (function(f){
      var fd=new FormData(); fd.append("f",f,f.name);
      fetch("/upload?dir="+encodeURIComponent(dir),{method:"POST",body:fd})
        .then(function(r){return r.json()})
        .then(function(d){ if(d.ok) ok++; })
        .catch(function(){})
        .then(function(){
          done++;
          if(done===total){ toast("上传完成 "+ok+"/"+total); loadFiles(dir); }
        });
    })(files[i]);
  }
  this.value="";
});

/* ========= 剪贴板 ========= */
$("clipGet").addEventListener("click",function(){ send({t:"get_clipboard"}); buzz("tap") });
$("clipSend").addEventListener("click",function(){
  send({t:"set_clipboard",text:$("clipArea").value});
  buzz("left"); toast("已发送到电脑剪贴板");
});
$("clipPaste").addEventListener("click",function(){ send({t:"key",value:"ctrl+v"}); buzz("tap") });

/* ========= 电源 ========= */
document.querySelectorAll("[data-power]").forEach(function(b){
  b.addEventListener("click",function(){
    var act=b.dataset.power;
    var label={lock:"锁定",sleep:"睡眠",signout:"注销",restart:"重启",shutdown:"关机"}[act]||act;
    if(act==="shutdown"||act==="restart"||act==="signout"){
      if(!confirm("确定要"+label+"电脑吗？")) return;
    }
    send({t:"power",action:act});
    buzz("heavy");
    toast("已发送："+label);
  });
});

/* ========= 主机状态 ========= */
function renderSysInfo(info){
  if(!info) return;
  var box=$("sysInfo"); box.innerHTML="";
  var items=[["CPU",info.cpu],["内存",info.ram],["运行",info.uptime],["主机",info.host]];
  if(info.battery && info.battery!=="—") items.push(["电量",info.battery]);
  items.forEach(function(kv){
    var c=document.createElement("span");
    c.className="chip"; c.style.pointerEvents="none";
    c.textContent=kv[0]+" "+kv[1];
    box.appendChild(c);
  });
}
'''

marker = "/* ========= 设置 ========= */"
assert marker in s, "设置标记未找到"
s = s.replace(marker, js + "\n" + marker, 1)

# 消息分发
old_case = '      case "volume":'
new_case = '''      case "clipboard":
        $("clipArea").value = m.text || "";
        toast((m.text||"").length ? "已读取电脑剪贴板" : "剪贴板为空");
        break;
      case "sysinfo":
        renderSysInfo(m.info);
        break;
      case "volume":'''
assert old_case in s
s = s.replace(old_case, new_case, 1)

# Dock 切页加载
old_dock = '    if(page==="mediaPage") send({t:"get_volume"});'
new_dock = '''    if(page==="mediaPage"){ send({t:"get_volume"}); send({t:"get_sysinfo"}); }
    if(page==="screenPage") loadScreen();
    if(page==="filesPage") loadFiles(fileCwd);'''
assert old_dock in s
s = s.replace(old_dock, new_dock, 1)

io.open(P, "w", encoding="utf-8").write(s)
print("JS 注入完成，总长度", len(s))
