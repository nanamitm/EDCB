#pragma once

#ifdef EDCB_USE_DARKMODELIB
#include <Darkmodelib.h>
#endif

//Windowsがダークモードのときはwin32-darkmodelibのダーク対応メッセージボックス(TaskDialog)で表示する
//それ以外は従来どおりMessageBox
inline int DarkAwareMessageBox(HWND hWnd, LPCWSTR lpText, LPCWSTR lpCaption, UINT uType)
{
#ifdef EDCB_USE_DARKMODELIB
	if( dmlib::isDarkModeReg() ){
		dmlib::initDarkMode();
		if( dmlib::isEnabled() ){
			//nullptrだと"Error"になるので、MessageBoxと同じく"エラー"にする
			return dmlib::darkMessageBoxW(hWnd, lpText, lpCaption ? lpCaption : L"エラー", uType);
		}
	}
#endif
	return MessageBox(hWnd, lpText, lpCaption, uType);
}
